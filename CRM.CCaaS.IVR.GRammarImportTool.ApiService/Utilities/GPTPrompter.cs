using System.Collections.Concurrent;
using System.IO.Compression;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities;

public class GPTPrompter
{
    private readonly IChatClient _chatClient;
    private readonly List<ChatMessage> _initialChatHistory;
    private readonly ILogger<GPTPrompter> _logger;
    private readonly int _degreeParallelism;

    public GPTPrompter(IConfiguration configuration, ILogger<GPTPrompter> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        var (endpoint, deployment, key) = GetOpenAiConfiguration(configuration);

        _chatClient = CreateChatClient(endpoint, deployment, key);
        _initialChatHistory = LoadInitialChatHistory(configuration);
        _degreeParallelism = GetDegreeParallelismFromConfig(configuration);
        _logger.LogInformation("GPTPrompter initialized successfully at {Timestamp}.", DateTime.UtcNow);
    }

    private int GetDegreeParallelismFromConfig(IConfiguration configuration)
    {
        var degreeParallelismValue = configuration["GPTPrompter:DegreeParallelism"];
        if (int.TryParse(degreeParallelismValue, out int degreeParallelism) && degreeParallelism > 0)
        {
            return degreeParallelism;
        }
        return 7; // Default value
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
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

        await progressCallback(0, "Starting processing...");

        int processedCount = 0;
        int totalCount = entries.Count;

        var parallelLoopResult = Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = _degreeParallelism }, entry =>
        {
            string content;
            if (!TryReadXmlContent(entry, out content))
            {
                results[entry.Name] = content;
            }
            else
            {
                try
                {
                    // Synchronously wait for async method (not ideal, but required for Parallel.ForEach)
                    string response = ProcessSingleFileAsync(entry.Name, content).GetAwaiter().GetResult();
                    results[entry.Name] = response;
                }
                catch (Exception ex)
                {
                    results[entry.Name] = $"Error: {ex.Message}";
                }
            }

            int current = Interlocked.Increment(ref processedCount);
            int progress = (int)(current / (double)totalCount * 100);
            // Fire and forget progress callback (do not await inside Parallel.ForEach)
            _ = progressCallback(progress, $"Processed {current} of {totalCount} files...");
        });

        _logger.LogInformation("Completed processing files in the zip archive at {Timestamp}.", DateTime.UtcNow);

        var outputStream = CreateResultZipStream(results);
        await completedCallback(outputStream.ToArray());
        outputStream.Position = 0;
        return outputStream;
    }

    /// <summary>
    /// Processes a single file by sending its content to the model and returning the response.
    /// </summary>
    private async Task<string> ProcessSingleFileAsync(string fileName, string fileContent)
    {
        _logger.LogInformation("Processing file content for entity type classification at {Timestamp}.", DateTime.UtcNow);
        var chatHistory = new List<ChatMessage>(_initialChatHistory)
        {
            new ChatMessage(ChatRole.User, $"Convert the file {fileName} to Microsoft Copilot Studio Yaml: {fileContent}")
        };

        var response = string.Empty;
        try
        {
            await foreach (var item in _chatClient.GetStreamingResponseAsync(chatHistory))
            {
                response += item.Text;
            }
            _logger.LogInformation("File content processed successfully at {Timestamp}.", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while processing file content at {Timestamp}.", DateTime.UtcNow);
            throw;
        }

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
    private bool TryReadXmlContent(ZipArchiveEntry entry, out string content)
    {
        try
        {
            using var entryStream = entry.Open();
            var xmlDoc = System.Xml.Linq.XDocument.Load(entryStream);
            if (xmlDoc.Root is not null)
            {
                RemoveCommentsAndWhitespace(xmlDoc.Root);
            }
            content = xmlDoc.ToString();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse {FileName} as XML at {Timestamp}.", entry.Name, DateTime.UtcNow);
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
    private static List<ChatMessage> LoadInitialChatHistory(IConfiguration config)
    {
        var chatHistorySection = config.GetSection("GPTPrompter:InitialChatHistory").GetChildren();
        var initialChatHistory = new List<ChatMessage>();
        foreach (var item in chatHistorySection)
        {
            var role = item["Role"];
            var content = item["Content"];
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
    /// Retrieves OpenAI configuration values and validates them.
    /// </summary>
    private (string Endpoint, string Deployment, string Key) GetOpenAiConfiguration(IConfiguration config)
    {
        string? endpoint = config["AZURE_OPENAI_ENDPOINT"];
        string? deployment = config["AZURE_OPENAI_GPT_NAME"];
        string? key = config["AZURE_OPENAI_GPT_KEY"];

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deployment) || string.IsNullOrEmpty(key))
        {
            _logger.LogError("Azure OpenAI configuration is missing.");
            throw new InvalidOperationException("Azure OpenAI configuration is missing.");
        }
        return (endpoint, deployment, key);
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
