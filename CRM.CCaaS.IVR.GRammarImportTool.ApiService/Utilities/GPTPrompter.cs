using Microsoft.Extensions.AI;
using Azure.AI.OpenAI;
using Azure;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities
{
    public class GPTPrompter
    {
        private readonly IChatClient _chatClient;
        private readonly List<ChatMessage> _initialChatHistory;
        private List<ChatMessage> _chatHistory;
        private readonly ILogger<GPTPrompter> _logger;

        public GPTPrompter(IConfiguration configuration, ILogger<GPTPrompter> logger)
        {
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
            _chatHistory = new List<ChatMessage>(_initialChatHistory);
            _logger.LogInformation("GPTPrompter initialized successfully at {Timestamp}.", DateTime.UtcNow);
        }

        public async Task<string> GetFileEntityTypeAsync(string fileName, string fileContent)
        {
            _logger.LogInformation("Processing file content for entity type classification at {Timestamp}.", DateTime.UtcNow);
            _chatHistory.Add(new ChatMessage(ChatRole.User, $"Convert the file {fileName} to Microsoft Copilot Studio Yaml: {fileContent}"));

            var response = "";
            try
            {
                await foreach (var item in _chatClient.GetStreamingResponseAsync(_chatHistory))
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
            finally
            {
                // Reset chat history to initial state
                _chatHistory = [.. _initialChatHistory];
            }

            return response;
        }
    }
}
