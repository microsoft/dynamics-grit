// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Concurrent;
using System.IO.Compression;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Azure;
using Azure.AI.OpenAI;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

[assembly: InternalsVisibleTo("CRM.CCaaS.IVR.GRammarImportTool.Tests.L0")]
namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;

public class GptChatGrxmlToMcsConverter : GptChatBase
{
    public const string SERVICE_KEY = "chat-grit";
    private const int RESULTS_CHANNEL_WRITER_TIMEOUT_SEC = 5;

    private readonly List<ChatTurn> _initialChatHistory;
    private readonly ILogger<GptChatGrxmlToMcsConverter> _logger;
    private readonly GptChatGrxmlConfiguration _gptPrompterConfiguration;
    private readonly string _disclaimerAI;

    /// <summary>
    /// Optional validator for AI content to enforce Responsible AI guardrails.
    /// </summary>
    public AiContentValidator AiContentValidator { get; set; }

    /// <summary>
    /// Validator for token count limits.
    /// </summary>
    public TokenValidator TokenValidator { get; private set; }

    public GptChatGrxmlToMcsConverter(
        IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration,
        IChatService openAIChatServiceFactory,
        AiContentValidator aiContentValidator,
        TokenValidator tokenValidator)
        : base(openAIChatServiceFactory, gptPrompterConfiguration.Value)
    {
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration);
        ArgumentNullException.ThrowIfNull(openAIChatServiceFactory);
        ArgumentNullException.ThrowIfNull(tokenValidator);

        _logger = GrITLoggerFactory.CreateLogger<GptChatGrxmlToMcsConverter>();
        _gptPrompterConfiguration = gptPrompterConfiguration.Value;

        _initialChatHistory = LoadInitialChatHistory(_gptPrompterConfiguration);
        _disclaimerAI = _gptPrompterConfiguration.DisclaimerAI ?? string.Empty;
        AiContentValidator = aiContentValidator;
        TokenValidator = tokenValidator;
        _logger.LogInformation("[Init] GptChatGrxmlToMcsConverter initialized with AI content validation and token validation");
    }

    private async Task<string> ProcessAndLogFileAsync(string fileName, string fileContent, string loggerContext, CancellationToken cancellationToken)
    {
        string content;
        var hashEnabled = _gptPrompterConfiguration.HashFileNameInLogs;
        var fileNameToLog = hashEnabled ? HashHelper.HashSha256Hex(fileName) : fileName;
        var stopWatch = new System.Diagnostics.Stopwatch();

        _logger.LogInformation("[{LoggingContext}] FileStart | HashedFileName={FileName}", loggerContext, fileNameToLog);
        if (!TryReadXmlContent(fileName, fileContent, out content))
        {
            _logger.LogError("[{LoggingContext}] XmlParseError | HashedFileName={FileName}", loggerContext, fileNameToLog);
            return $"<!-- Can't parse this XML -->\n{fileContent}";
        }
        try
        {
            stopWatch.Start();
            var response = await ProcessSingleFileAsync(fileName, content, cancellationToken);
            _logger.LogInformation("[{LoggingContext}] FileProcessed | HashedFileName={FileName} | ElapsedMs={ElapsedMilliseconds}",
                loggerContext, fileNameToLog, stopWatch.ElapsedMilliseconds);
            return response;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[{LoggingContext}] Cancelled | HashedFileName={FileName}", loggerContext, fileNameToLog);
            return $"Error: Processing cancelled for {fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{LoggingContext}] UnexpectedError | HashedFileName={FileName}", loggerContext, fileNameToLog);
            return $"Error: {ex.Message}";
        }
        finally
        {
            stopWatch.Stop();
            _logger.LogInformation("[{LoggingContext}] FileEnd | HashedFileName={FileName} | TotalElapsedMs={ElapsedMilliseconds}",
                loggerContext, fileNameToLog, stopWatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Processes a zip file containing multiple files, calls the model for each file in parallel batches, and returns a zip file containing one text file per entry with the model response.
    /// </summary>
    /// <param name="zipStream">A stream containing the zip file data.</param>
    /// <param name="progressCallback">Callback for progress updates.</param>
    /// <param name="completedCallback">Callback when processing is complete.</param>
    /// <returns>A stream containing a zip file with one text file per entry.</returns>
    public override async Task<Stream> ConvertZipAsync(
        Stream zipStream,
        Func<int, string, Task> progressCallback,
        Func<byte[], Task> completedCallback)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        ArgumentNullException.ThrowIfNull(progressCallback);
        ArgumentNullException.ThrowIfNull(completedCallback);

        var results = new ConcurrentDictionary<string, string>();
        var entries = await LoadZipToDictionaryAsync(zipStream);

        await progressCallback(0, "Starting processing...");

        _logger.LogInformation("[ConvertZipAsync] Start | FileCount={FileCount} | DegreeParallelism={DegreeParallelism}",
            entries.Count, _gptPrompterConfiguration.DegreeParallelism);

        var processedCount = 0;
        var totalCount = entries.Count;

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(_gptPrompterConfiguration.MaxAllowedConversionTimeTotalSec));
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _gptPrompterConfiguration.DegreeParallelism,
            CancellationToken = cts.Token
        };

        try
        {
            await Parallel.ForEachAsync(entries, options, async (entry, token) =>
            {
                var response = await ProcessAndLogFileAsync(entry.Key, entry.Value, "ConvertZipAsync", token);
                results[entry.Key] = response;

                var current = Interlocked.Increment(ref processedCount);
                var progress = (int)(current / (double)totalCount * 100);
                // Fire and forget progress callback (do not await inside Parallel.ForEach)
                await progressCallback(progress, $"{entry.Key}|{current} of {totalCount} files...");
            });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[ConvertZipAsync] Cancelled | Reason=TotalTimeout");
            results["error.yaml"] = "Error: Zip file processing cancelled";
        }
        _logger.LogInformation("[ConvertZipAsync] Complete | FileCount={FileCount}", totalCount);

        var outputStream = await CreateResultZipStreamAsync(results, ".yaml");
        await completedCallback(outputStream.ToArray());
        outputStream.Position = 0;
        return outputStream;
    }

    /// <summary>
    /// Processes a single GRXML file by sending its content to the model, tracks progress, and returns the converted YAML as a string.
    /// </summary>
    /// <param name="stringFile">The GRXML file content as a string.</param>
    /// <param name="progressCallback">Callback for reporting progress updates.</param>
    /// <param name="completedCallback">Callback invoked when processing is complete, with the result.</param>
    /// <returns>The converted YAML content as a string.</returns>
    public override async Task<string> ConvertFileAsync(string stringFile, Func<int, string, Task> progressCallback, Func<string, Task> completedCallback)
    {
        ArgumentException.ThrowIfNullOrEmpty(stringFile);
        ArgumentNullException.ThrowIfNull(progressCallback);
        ArgumentNullException.ThrowIfNull(completedCallback);

        _logger.LogInformation("[ConvertFileAsync] Start");
        await progressCallback(0, "Starting processing...");

        var processedResult = await ProcessAndLogFileAsync("ConvertToYaml.grxml", stringFile, "ConvertFileAsync", CancellationToken.None);
        await completedCallback(processedResult);

        _logger.LogInformation("[ConvertFileAsync] Done");
        return processedResult;
    }

    /// <summary>
    /// Processes a single file by sending its content to the model and returning the response.
    /// </summary>
    /// <param name="fileName">Name of the file (for logging / context).</param>
    /// <param name="fileContent">Content of the GRXML file.</param>
    /// <param name="cancellationToken">External cancellation token to observe in addition to the per-file timeout.</param>
    internal virtual async Task<string> ProcessSingleFileAsync(string fileName, string fileContent, CancellationToken cancellationToken = default)
    {
        var hashEnabled = _gptPrompterConfiguration.HashFileNameInLogs;
        var fileNameToLog = hashEnabled ? HashHelper.HashSha256Hex(fileName) : fileName;

        _logger.LogInformation("[ProcessSingleFileAsync] Start | HashedFileName={FileName}", fileNameToLog);

        // Create the prompt for the AI model
        string prompt = $"Convert the file {fileName} to Microsoft Copilot Studio Yaml: {fileContent}";

        // If AI content validator is available, use it to validate and sanitize the prompt
        if (AiContentValidator != null)
        {
            var validationResult = await AiContentValidator.ValidateAiPromptAsync(prompt, fileName);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[ProcessSingleFileAsync] AI prompt validation failed | HashedFileName={FileName} | Reason={Reason}",
                    fileNameToLog, validationResult.ErrorMessage);
                return $"Error: {validationResult.ErrorMessage}";
            }

            // Sanitize the prompt to ensure it cannot be used for prompt injection
            prompt = AiContentValidator.SanitizeAiPrompt(prompt);
        }

        if (!IsTokenCountValid(prompt, fileNameToLog, out var tokenError))
        {
            return tokenError ?? "Unknown error while counting tokens.";
        }

        var chatHistory = new List<ChatTurn>(_initialChatHistory)
        {
            new ChatTurn("user", prompt)
        };

        var response = string.IsNullOrWhiteSpace(_disclaimerAI)
            ? string.Empty
            : $"\n#{_disclaimerAI}\n\n";

        // Create a timeout CTS and link with external token
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_gptPrompterConfiguration.MaxAllowedConversionTimeSingleFileSec));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        var retries = 0;
        while (retries < _gptPrompterConfiguration.MaxRetries)
        {
            try
            {
                await foreach (var item in OpenAIChatService.StreamAsync(chatHistory, linkedCts.Token))
                {
                    response += item;
                }
                _logger.LogInformation("[ProcessSingleFileAsync] Success | HashedFileName={FileName}", fileNameToLog);
                ValidateYamlContent(response);
                return response;
            }
            catch (System.ClientModel.ClientResultException ex)
            {
                _logger.LogWarning("[ProcessSingleFileAsync] ClientError | HashedFileName={FileName} | Error={Error}", fileNameToLog, ex.Message);
            }
            catch (YamlException ex)
            {
                _logger.LogWarning("[ProcessSingleFileAsync] YamlValidationFailed | HashedFileName={FileName} | Error={Error}", fileNameToLog, ex.Message);
            }
            catch (OperationCanceledException ex)
            {
                var reason = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                    ? "Timeout"
                    : "Cancelled";
                _logger.LogWarning(ex, "[ProcessSingleFileAsync] Cancelled | Reason={Reason} | HashedFileName={FileName}", reason, fileNameToLog);
                response += $"Error: Processing {reason.ToLowerInvariant()} for {fileName}";
                return response;
            }

            response = string.Empty;
            retries++;
            await Task.Delay(TimeSpan.FromSeconds(_gptPrompterConfiguration.RetryDelaySec * retries), cancellationToken);
        }

        _logger.LogError("[ProcessSingleFileAsync] MaxRetriesExceeded | HashedFileName={FileName} | Retries={Retries}", fileNameToLog, _gptPrompterConfiguration.MaxRetries);
        response += $"Error: Failed to process {fileName} after {_gptPrompterConfiguration.MaxRetries} retries.";
        return response;
    }

    /// <summary>
    /// Loads the initial chat history from configuration.
    /// </summary>
    private List<ChatTurn> LoadInitialChatHistory(GptChatGrxmlConfiguration config)
    {
        var initialChatHistory = new List<ChatTurn>();
        foreach (var item in config.InitialChatHistory!)
        {
            var role = item.Role;
            var content = item.Content;
            if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(content))
            {
                initialChatHistory.Add(new ChatTurn(role, content));
            }
        }
        return initialChatHistory;
    }

    /// <summary>
    /// Validates the provided YAML content by attempting to deserialize it.
    /// Throws a YamlException if the content is invalid.
    /// </summary>
    internal virtual void ValidateYamlContent(string yamlContent)
    {
        var deserializer = new DeserializerBuilder()
         .WithNamingConvention(CamelCaseNamingConvention.Instance)
         .Build();
        _ = deserializer.Deserialize<object>(yamlContent);
    }

    public override async Task<string> ConvertZipAsync(Stream zipStream, Channel<KeyValuePair<string, string>> results)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        ArgumentNullException.ThrowIfNull(results);

        var entries = await LoadZipToDictionaryAsync(zipStream);

        _logger.LogInformation("[ConvertZipAsync-Channel] Start | FileCount={FileCount} | DegreeParallelism={DegreeParallelism}",
            entries.Count, _gptPrompterConfiguration.DegreeParallelism);

        var processedCount = 0;
        var totalCount = entries.Count;

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(_gptPrompterConfiguration.MaxAllowedConversionTimeTotalSec));
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _gptPrompterConfiguration.DegreeParallelism,
            CancellationToken = cts.Token
        };

        try
        {
            await Parallel.ForEachAsync(entries, options, async (entry, token) =>
            {
                var response = await ProcessAndLogFileAsync(entry.Key, entry.Value, "ConvertZipAsync-Channel", token);
                using var ctsWrite = new CancellationTokenSource(TimeSpan.FromSeconds(RESULTS_CHANNEL_WRITER_TIMEOUT_SEC));
                await results.Writer.WriteAsync(new KeyValuePair<string, string>(entry.Key, response), ctsWrite.Token);

                var current = Interlocked.Increment(ref processedCount);
                var progress = (int)(current / (double)totalCount * 100);
                _logger.LogInformation("[ConvertZipAsync-Channel] Progress | Current={Current} | Total={Total} | Percent={Percent}",
                    current, totalCount, progress);
            });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[ConvertZipAsync-Channel] Cancelled | Reason=TotalTimeout");

            using var ctsWrite = new CancellationTokenSource(TimeSpan.FromSeconds(RESULTS_CHANNEL_WRITER_TIMEOUT_SEC));
            await results.Writer.WriteAsync(new KeyValuePair<string, string>("error.yaml", "Error: Zip file processing cancelled"), ctsWrite.Token);
        }
        finally
        {
            results.Writer.Complete();
        }
        _logger.LogInformation("[ConvertZipAsync-Channel] Complete | Processed={ProcessedCount} | Total={TotalCount}",
            processedCount, totalCount);
        _logger.LogInformation("[ConvertZipAsync-Channel] AllFilesProcessed");
        return "Conversion complete";
    }

    public override async Task<string> ConvertFileAsync(string stringFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(stringFile);

        var processedResult = await ProcessAndLogFileAsync("ConvertedFile.grxml", stringFile, "ConvertFileAsync", CancellationToken.None);
        return processedResult;
    }

    private bool IsTokenCountValid(string prompt, string fileNameToLog, out string? errorMessage)
    {
        errorMessage = null;
        if (TokenValidator != null)
        {
            var tokenValidationResult = TokenValidator.ValidateTokenCount(prompt, fileNameToLog);
            if (!tokenValidationResult.IsValid)
            {
                _logger.LogWarning("[ProcessSingleFileAsync] Token validation failed | HashedFileName={FileName} | Reason={Reason}",
                    fileNameToLog, tokenValidationResult.ErrorMessage);
                errorMessage = $"Error: {tokenValidationResult.ErrorMessage}";
                return false;
            }
        }
        return true;
    }
}
