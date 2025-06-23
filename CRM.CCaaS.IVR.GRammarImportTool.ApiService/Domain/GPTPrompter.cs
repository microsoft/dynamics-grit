using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Azure;
using Azure.AI.OpenAI;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;

public class GPTPrompter
{
    private readonly IChatClient _chatClient;
    private readonly List<ChatMessage> _initialChatHistory;
    private readonly ILogger<GPTPrompter> _logger;
    private readonly GPTPrompterConfiguration _gptPrompterConfiguration;

    public GPTPrompter(ILogger<GPTPrompter> logger, IOptions<GPTPrompterConfiguration> gptPrompterConfiguration)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration);

        _logger = logger;
        _gptPrompterConfiguration = gptPrompterConfiguration.Value;

        if (string.IsNullOrEmpty(_gptPrompterConfiguration.AzureOpenAIEndpoint)
            || string.IsNullOrEmpty(_gptPrompterConfiguration.AzureOpenAIDeploymentName)
            || string.IsNullOrEmpty(_gptPrompterConfiguration.AzureOpenAIKey))
        {
            _logger.LogError("Azure OpenAI configuration is missing.");
            throw new InvalidOperationException("Azure OpenAI configuration is missing.");
        }

        _chatClient = CreateChatClient(_gptPrompterConfiguration.AzureOpenAIEndpoint,
            _gptPrompterConfiguration.AzureOpenAIDeploymentName, _gptPrompterConfiguration.AzureOpenAIKey);
        _initialChatHistory = LoadInitialChatHistory(_gptPrompterConfiguration);
        _logger.LogInformation("GPTPrompter initialized successfully at {Timestamp}.", DateTime.UtcNow);
    }

    /// <summary>
    /// Processes a zip file containing multiple files, calls the model for each file in parallel batches, and returns a zip file containing one text file per entry with the model response.
    /// </summary>
    /// <param name="zipStream">A stream containing the zip file data.</param>
    /// <param name="progressCallback">Callback for progress updates.</param>
    /// <param name="completedCallback">Callback when processing is complete.</param>
    /// <returns>A stream containing a zip file with one text file per entry.</returns>
    public async Task<Stream> GetFileEntityTypeZipAsync(
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

        int processedCount = 0;
        int totalCount = entries.Count;

        var parallelLoopResult = Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = _gptPrompterConfiguration.DegreeParallelism }, async entry =>
        {
            string content;
            var stopWatch = new System.Diagnostics.Stopwatch();
            if (!TryReadXmlContent(entry, out content))
            {
                results[entry.Key] = entry.Value;
            }
            else
            {
                try
                {
                    stopWatch.Start();
                    // Synchronously wait for async method (not ideal, but required for Parallel.ForEach)
                    string response = ProcessSingleFileAsync(entry.Key, content).Result;
                    results[entry.Key] = response;
                    _logger.LogInformation("Processed file {FileName} in {ElapsedMilliseconds} ms at {Timestamp}.", entry.Key, stopWatch.ElapsedMilliseconds, DateTime.UtcNow);
                    stopWatch.Stop();
                }
                catch (Exception ex)
                {
                    results[entry.Key] = $"Error: {ex.Message}";
                }
            }

            int current = Interlocked.Increment(ref processedCount);
            int progress = (int)(current / (double)totalCount * 100);
            // Fire and forget progress callback (do not await inside Parallel.ForEach)
            await progressCallback(progress, $"Processed {current} of {totalCount} files...");
        });

        while (!parallelLoopResult.IsCompleted)
        {
            // Wait for all tasks to complete
            await Task.Delay(100);
        }
        _logger.LogInformation("Completed processing files in the zip archive at {Timestamp}.", DateTime.UtcNow);

        var outputStream = CreateResultZipStream(results);
        await completedCallback(outputStream.ToArray());
        outputStream.Position = 0;
        return outputStream;
    }

    private Dictionary<string, string> LoadZipToDictionary(Stream zipStream)
    {
        Dictionary<string, string> entries = new Dictionary<string, string>();

        using (var a = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            foreach (var entry in a.Entries)
            {
                using (var entryStream = entry.Open())
                using (var reader = new StreamReader(entryStream, Encoding.UTF8))
                {
                    string content = reader.ReadToEnd();
                    entries.Add(entry.Name, content);
                }
            }
        }
        return entries;
    }

    /// <summary>
    /// Processes a single file by sending its content to the model and returning the response.
    /// </summary>
    private async Task<string> ProcessSingleFileAsync(string fileName, string fileContent)
    {
        _logger.LogInformation("Processing file content for entity type classification at {Timestamp}.", DateTime.UtcNow);
        var retries = _gptPrompterConfiguration.MaxRetries;

        var chatHistory = new List<ChatMessage>(_initialChatHistory)
        {
            new ChatMessage(ChatRole.User, $"Convert the file {fileName} to Microsoft Copilot Studio Yaml: {fileContent}")
        };

        var response = string.Empty;
        while (retries > 0)
        {
            try
            {
                await foreach (var item in _chatClient.GetStreamingResponseAsync(chatHistory))
                {
                    response += item.Text;
                }
                _logger.LogInformation("File content processed successfully at {Timestamp}.", DateTime.UtcNow);
                return response;
            }
            catch (System.ClientModel.ClientResultException ex)
            {
                _logger.LogWarning("Client error {Error} occurred while processing file content at {Timestamp}. \n Will retry", ex, DateTime.UtcNow);
                response = string.Empty;
                retries--;
                await Task.Delay(TimeSpan.FromSeconds(_gptPrompterConfiguration.RetryDelaySec));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while processing file content at {Timestamp}.", DateTime.UtcNow);
                throw;
            }
        }
        _logger.LogError("Failed to process file {FileName} after {Retries} retries at {Timestamp}.", fileName, _gptPrompterConfiguration.MaxRetries, DateTime.UtcNow);
        response += $"Error: Failed to process {fileName} after {_gptPrompterConfiguration.MaxRetries} retries.";
        return response;
    }

    /// <summary>
    /// Removes comments and whitespace from an XML element and its descendants.
    /// </summary>
    private static void RemoveCommentsAndWhitespace(System.Xml.Linq.XElement element)
    {
        if (element == null) return;
        foreach (var node in element.DescendantNodes().OfType<System.Xml.Linq.XComment>().ToList())
        {
            node.Remove();
        }
        foreach (var node in element.DescendantNodes().OfType<System.Xml.Linq.XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList())
        {
            node.Remove();
        }
    }

    /// <summary>
    /// Reads and cleans XML content from a ZipArchiveEntry.
    /// </summary>
    private bool TryReadXmlContent(KeyValuePair<string, string> entry, out string content)
    {
        try
        {
            var xmlDoc = System.Xml.Linq.XDocument.Parse(entry.Value);
            if (xmlDoc.Root is not null)
            {
                RemoveCommentsAndWhitespace(xmlDoc.Root);
            }
            content = xmlDoc.ToString();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse {FileName} as XML at {Timestamp}.", entry.Key, DateTime.UtcNow);
            content = $"Error: Failed to parse as XML. {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Creates a MemoryStream containing a zip archive with one text file per result.
    /// </summary>
    private static MemoryStream CreateResultZipStream(ConcurrentDictionary<string, string> results)
    {
        var outputStream = new MemoryStream();
        using (var outputArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var kvp in results)
            {
                var entry = outputArchive.CreateEntry(Path.GetFileNameWithoutExtension(kvp.Key) + ".yaml");
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(kvp.Value);
            }
        }
        outputStream.Position = 0;
        return outputStream;
    }

    /// <summary>
    /// Loads the initial chat history from configuration.
    /// </summary>
    private static List<ChatMessage> LoadInitialChatHistory(GPTPrompterConfiguration config)
    {
        var initialChatHistory = new List<ChatMessage>();
        foreach (var item in config.InitialChatHistory!)
        {
            var role = item.Role;
            var content = item.Content;
            if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(content))
            {
                ChatRole chatRole = role.ToLower() switch
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
    /// Creates the chat client for OpenAI.
    /// </summary>
    private static IChatClient CreateChatClient(string endpoint, string deployment, string key)
    {
        return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
            .AsChatClient(deployment);
    }
}
