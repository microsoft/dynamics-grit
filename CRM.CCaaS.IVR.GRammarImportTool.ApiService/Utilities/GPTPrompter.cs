using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure;
using System.IO.Compression;
using System.Collections.Concurrent;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities;

public class GPTPrompter
{
    private readonly IChatClient _chatClient;
    private readonly List<ChatMessage> _initialChatHistory;
    private List<ChatMessage> _chatHistory;
    private readonly ILogger<GPTPrompter> _logger;

    public GPTPrompter(IConfiguration configuration, ILogger<GPTPrompter> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger;
        var config = configuration;
        string? endpoint = config["AZURE_OPENAI_ENDPOINT"];
        string? deployment = config["AZURE_OPENAI_GPT_NAME"];
        string? key = config["AZURE_OPENAI_GPT_KEY"];

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deployment) || string.IsNullOrEmpty(key))
        {
            _logger.LogError("Azure OpenAI configuration is missing.");
            throw new InvalidOperationException("Azure OpenAI configuration is missing.");
        }

        _logger.LogInformation("Initializing Azure OpenAI client at {Timestamp}.", DateTime.UtcNow);
        _chatClient =
            new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key))
                .AsChatClient(deployment);

        // Read InitialChatHistory from configuration
        var chatHistorySection = config.GetSection("GPTPrompter:InitialChatHistory").GetChildren();
        _initialChatHistory = new List<ChatMessage>();
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
                _initialChatHistory.Add(new ChatMessage(chatRole, content));
            }
        }
        _chatHistory = [.. _initialChatHistory];
        _logger.LogInformation("GPTPrompter initialized successfully at {Timestamp}.", DateTime.UtcNow);
    }

    /// <summary>
    /// Processes a zip file containing multiple files, calls the model for each file in parallel batches of 4, and returns a zip file containing one text file per entry with the model response.
    /// </summary>
    /// <param name="zipStream">A stream containing the zip file data.</param>
    /// <returns>A stream containing a zip file with one text file per entry.</returns>
    public async Task<Stream> GetFileEntityTypeZipAsync(Stream zipStream,
        Func<int, string, Task> progressCallback,
        Func<byte[], Task> completedCallback)
    {
        ArgumentNullException.ThrowIfNull(completedCallback);
        ArgumentNullException.ThrowIfNull(progressCallback);

        var results = new ConcurrentDictionary<string, string>();
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        int batchSize = 4;

        await progressCallback(0, "Starting processing...");

        for (int i = 0; i < entries.Count; i += batchSize)
        {
            int progress = (int)((i + batchSize) / (double)entries.Count * 100);

            // if (i % 20 == 0) { 
            await progressCallback(progress, $"Processing batch {i / batchSize + 1} of {Math.Ceiling(entries.Count / (double)batchSize)}...");
            //}

            var batch = entries.Skip(i).Take(batchSize).ToList();
            _logger.LogInformation("Processing batch {BatchIndex} of files at {Timestamp}.", i / batchSize + 1, DateTime.UtcNow);
            // Step 1: Read all file contents in this batch sequentially
            var fileContents = new List<(string FileName, string Content)>();
            foreach (var entry in batch)
            {
                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream);
                string fileContent = reader.ReadToEnd();
                fileContents.Add((entry.Name, fileContent));
            }

            // Step 2: Process the files in parallel
            var tasks = fileContents.Select(async file =>
            {
                try
                {
                    string response = await ProcessSingleFileAsync(file.FileName, file.Content);
                    results[file.FileName] = response;
                }
                catch (Exception ex)
                {
                    results[file.FileName] = $"Error: {ex.Message}";
                }
            }).ToList();

            await Task.WhenAll(tasks);
        }

        _logger.LogInformation("Completed processing files in the zip archive at {Timestamp}.", DateTime.UtcNow);

        // Create a new zip archive in memory with one text file per result
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
        await completedCallback(outputStream.ToArray());
        return outputStream;
    }

    // Helper method to process a single file (extracted from the original method)
    private async Task<string> ProcessSingleFileAsync(string fileName, string fileContent)
    {
        _logger.LogInformation("Processing file content for entity type classification at {Timestamp}.", DateTime.UtcNow);
        var localChatHistory = new List<ChatMessage>(_initialChatHistory)
            {
                new ChatMessage(ChatRole.User, $"Convert the file {fileName} to Microsoft Copilot Studio Yaml: {fileContent}")
            };

        var response = "";
        try
        {
            await foreach (var item in _chatClient.GetStreamingResponseAsync(localChatHistory))
            {
                Console.Write(item.Text);
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
}
