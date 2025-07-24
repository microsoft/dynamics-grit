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
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
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

    private readonly IChatClient _chatClient;
    private readonly List<ChatMessage> _initialChatHistory;
    private readonly ILogger<GptChatGrxmlToMcsConverter> _logger;
    private readonly GptChatGrxmlConfiguration _gptPrompterConfiguration;
    private readonly string _disclaimerAI;

    public GptChatGrxmlToMcsConverter(
        IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration,
        IAzureOpenAIClientFactory azureOpenAIClientFactory) : base(azureOpenAIClientFactory)
    {
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration);
        ArgumentNullException.ThrowIfNull(azureOpenAIClientFactory);

        _logger = GrITLoggerFactory.CreateLogger<GptChatGrxmlToMcsConverter>();
        _gptPrompterConfiguration = gptPrompterConfiguration.Value;

        _chatClient = azureOpenAIClientFactory.CreateChatClient(_gptPrompterConfiguration.AzureOpenAIEndpoint,
            _gptPrompterConfiguration.AzureOpenAIDeploymentName, _gptPrompterConfiguration.AzureOpenAIKey);
        _initialChatHistory = LoadInitialChatHistory(_gptPrompterConfiguration);
        _disclaimerAI = _gptPrompterConfiguration.DisclaimerAI ?? string.Empty;
        _logger.LogInformation("GptChatGrxmlToMcsConverter initialized successfully at {Timestamp}.", DateTime.UtcNow);
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

        var entries = LoadZipToDictionary(zipStream);

        await progressCallback(0, "Starting processing...");

        _logger.LogInformation("Starting processing files at {Timestamp} with {_gptPrompterConfiguration.DegreeParallelism} parallel tasks", DateTime.UtcNow, _gptPrompterConfiguration.DegreeParallelism);

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
                string content;
                var stopWatch = new System.Diagnostics.Stopwatch();

                _logger.LogInformation("Starting to process file {FileName}.", entry.Key);
                if (!TryReadXmlContent(entry.Key, entry.Value, out content))
                {
                    _logger.LogError("Failed to read XML content from the entry {FileName} at {Timestamp}.", entry.Key, DateTime.UtcNow);
                    results[entry.Key] = $"<!-- Can't parse this XML -->\n{entry.Value}";
                }
                else
                {
                    try
                    {
                        stopWatch.Start();
                        var response = await ProcessSingleFileAsync(entry.Key, content);
                        results[entry.Key] = response;
                        _logger.LogInformation("Processed file {FileName} in {ElapsedMilliseconds} ms at {Timestamp}.", entry.Key, stopWatch.ElapsedMilliseconds, DateTime.UtcNow);
                        stopWatch.Stop();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error occurred while processing file content at {Timestamp}.", DateTime.UtcNow);
                        results[entry.Key] = $"Error: Unexpected error occured";
                    }
                }

                var current = Interlocked.Increment(ref processedCount);
                var progress = (int)(current / (double)totalCount * 100);
                // Fire and forget progress callback (do not await inside Parallel.ForEach)
                await progressCallback(progress, $"{entry.Key}|{stopWatch.ElapsedMilliseconds}|{current} of {totalCount} files...");
            });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Processing was cancelled for zip at {Timestamp}.", DateTime.UtcNow);
            results["error.yaml"] = "Error: Zip file processing cancelled";
        }
        _logger.LogInformation("Completed processing files in the zip archive at {Timestamp}.", DateTime.UtcNow);

        var outputStream = CreateResultZipStream(results, ".yaml");
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

        _logger.LogInformation("Starting to process single file");
        if (!TryReadXmlContent("ConvertToYaml.grxml", stringFile, out var stringStrippedFile))
        {
            _logger.LogError("Failed to read XML content at {Timestamp}.", DateTime.UtcNow);
            return stringStrippedFile;
        }

        await progressCallback(0, "Starting processing...");

        var processedResult = await ProcessSingleFileAsync("ConvertToYaml.grxml", stringStrippedFile);
        await completedCallback(processedResult);

        return processedResult;
    }

    /// <summary>
    /// Processes a single file by sending its content to the model and returning the response.
    /// </summary>
    internal virtual async Task<string> ProcessSingleFileAsync(string fileName, string fileContent)
    {
        _logger.LogInformation("Processing file content for entity type classification at {Timestamp}.", DateTime.UtcNow);

        var chatHistory = new List<ChatMessage>(_initialChatHistory)
        {
            new ChatMessage(ChatRole.User, $"Convert the file {fileName} to Microsoft Copilot Studio Yaml: {fileContent}")
        };

        var response = string.IsNullOrWhiteSpace(_disclaimerAI)
            ? string.Empty
            : $"\n#{_disclaimerAI}\n\n";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_gptPrompterConfiguration.MaxAllowedConversionTimeSingleFileSec));
        var retries = 0;
        while (retries < _gptPrompterConfiguration.MaxRetries)
        {
            try
            {
                await foreach (var item in _chatClient.GetStreamingResponseAsync(chatHistory, null, cts.Token))
                {
                    response += item.Text;
                }
                _logger.LogInformation("File content processed successfully at {Timestamp}.", DateTime.UtcNow);
                ValidateYamlContent(response);
                return response;
            }
            catch (System.ClientModel.ClientResultException ex)
            {
                _logger.LogWarning("Client error {Error} occurred while processing file content at {Timestamp}. \n Will retry", ex, DateTime.UtcNow);
            }
            catch (YamlException ex)
            {
                _logger.LogWarning("Yaml validation failed: {Message}", ex.Message);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Processing was cancelled for file {FileName} at {Timestamp}.", fileName, DateTime.UtcNow);
                response += $"Error: Processing cancelled for {fileName}";
                return response;
            }
            response = string.Empty;
            retries++;
            await Task.Delay(TimeSpan.FromSeconds(_gptPrompterConfiguration.RetryDelaySec * retries));
        }
        _logger.LogError("Failed to process file {FileName} after {Retries} retries at {Timestamp}.", fileName, _gptPrompterConfiguration.MaxRetries, DateTime.UtcNow);
        response += $"Error: Failed to process {fileName} after {_gptPrompterConfiguration.MaxRetries} retries.";
        return response;
    }

    /// <summary>
    /// Loads the initial chat history from configuration.
    /// </summary>
    private List<ChatMessage> LoadInitialChatHistory(GptChatGrxmlConfiguration config)
    {
        var initialChatHistory = new List<ChatMessage>();
        foreach (var item in config.InitialChatHistory!)
        {
            var role = item.Role;
            var content = item.Content;
            if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(content))
            {
                var chatRole = role.ToLower() switch
                {
                    "system" => ChatRole.System,
                    "user" => ChatRole.User,
                    "assistant" => ChatRole.Assistant,
                    _ => ChatRole.System
                };
                initialChatHistory.Add(new ChatMessage(chatRole, content));
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

        var entries = LoadZipToDictionary(zipStream);

        _logger.LogInformation("Starting processing files at {Timestamp} with {_gptPrompterConfiguration.DegreeParallelism} parallel tasks", DateTime.UtcNow, _gptPrompterConfiguration.DegreeParallelism);

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
                string content;
                var stopWatch = new System.Diagnostics.Stopwatch();
                if (!TryReadXmlContent(entry.Key, entry.Value, out content))
                {
                    _logger.LogError("Failed to read XML content from the entry {FileName} at {Timestamp}.", entry.Key, DateTime.UtcNow);
                    using var ctsWrite = new CancellationTokenSource(TimeSpan.FromSeconds(RESULTS_CHANNEL_WRITER_TIMEOUT_SEC));
                    await results.Writer.WriteAsync(new KeyValuePair<string, string>(entry.Key, $"<!-- Can't parse this XML -->\n{entry.Value}"), ctsWrite.Token);
                }
                else
                {
                    try
                    {
                        stopWatch.Start();
                        var response = ProcessSingleFileAsync(entry.Key, content).Result;

                        using var ctsWrite = new CancellationTokenSource(TimeSpan.FromSeconds(RESULTS_CHANNEL_WRITER_TIMEOUT_SEC));
                        await results.Writer.WriteAsync(new KeyValuePair<string, string>(entry.Key, response), ctsWrite.Token);

                        _logger.LogInformation("Processed file {FileName} in {ElapsedMilliseconds} ms at {Timestamp}.", entry.Key, stopWatch.ElapsedMilliseconds, DateTime.UtcNow);
                        stopWatch.Stop();
                    }
                    catch (OperationCanceledException ex)
                    {
                        _logger.LogError(ex, "Processing was cancelled for file {FileName} at {Timestamp}.", entry.Key, DateTime.UtcNow);

                        using var ctsWrite = new CancellationTokenSource(TimeSpan.FromSeconds(RESULTS_CHANNEL_WRITER_TIMEOUT_SEC));
                        await results.Writer.WriteAsync(new KeyValuePair<string, string>(entry.Key, $"Error: Processing cancelled for {entry.Key}"), ctsWrite.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error occurred while processing file content at {Timestamp}.", DateTime.UtcNow);

                        using var ctsWrite = new CancellationTokenSource(TimeSpan.FromSeconds(RESULTS_CHANNEL_WRITER_TIMEOUT_SEC));
                        await results.Writer.WriteAsync(new KeyValuePair<string, string>(entry.Key, $"Error: {ex.Message}"), ctsWrite.Token);
                    }
                }

                var current = Interlocked.Increment(ref processedCount);
                var progress = (int)(current / (double)totalCount * 100);
                _logger.LogInformation("Processed {Current} of {Total} files. Progress: {Progress}%", current, totalCount, progress);
            });
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Processing was cancelled for zip at {Timestamp}.", DateTime.UtcNow);

            using var ctsWrite = new CancellationTokenSource(TimeSpan.FromSeconds(RESULTS_CHANNEL_WRITER_TIMEOUT_SEC));
            await results.Writer.WriteAsync(new KeyValuePair<string, string>("error.yaml", "Error: Zip file processing cancelled"), ctsWrite.Token);
        }
        finally
        {
            results.Writer.Complete();
        }
        _logger.LogInformation("Processed {ProcessedCount} of {TotalCount} files at {Timestamp}.", processedCount, totalCount, DateTime.UtcNow);
        _logger.LogInformation("Completed processing files in the zip archive at {Timestamp}.", DateTime.UtcNow);
        return "Conversion complete";
    }

    public override async Task<string> ConvertFileAsync(string stringFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(stringFile);

        if (!TryReadXmlContent("ConvertedFile.grxml", stringFile, out var stringStrippedFile))
        {
            _logger.LogError("Failed to read XML content from the provided string file at {Timestamp}.", DateTime.UtcNow);
            return stringStrippedFile;
        }

        _logger.LogInformation("Starting processing file at {Timestamp}", DateTime.UtcNow);
        var processedResult = await ProcessSingleFileAsync("ConvertedFile.grxml", stringStrippedFile);
        _logger.LogInformation("File processed successfully at {Timestamp}", DateTime.UtcNow);

        return processedResult;
    }
}
