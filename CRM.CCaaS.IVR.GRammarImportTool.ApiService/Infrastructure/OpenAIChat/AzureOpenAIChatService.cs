// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using AzureClient = Azure.AI.OpenAI.AzureOpenAIClient;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;

public sealed class AzureOpenAIChatService : IChatService
{
    private readonly IChatClient _chatClient;
    private readonly IOptions<GptChatGrxmlConfiguration> _gptChatConfiguration;
    private readonly ILogger<AzureOpenAIChatService> _logger;

    public AzureOpenAIChatService(IOptions<GptChatGrxmlConfiguration> gptChatConfiguration, IAzureOpenAIClientFactory azureOpenAIClientFactory)
    {
        ArgumentNullException.ThrowIfNull(gptChatConfiguration?.Value, nameof(gptChatConfiguration));
        ArgumentNullException.ThrowIfNull(azureOpenAIClientFactory, nameof(azureOpenAIClientFactory));

        _gptChatConfiguration = gptChatConfiguration;
        var options = _gptChatConfiguration.Value;

        _chatClient = azureOpenAIClientFactory.CreateChatClient(options);
        _logger = GrITLoggerFactory.CreateLogger<AzureOpenAIChatService>();
        _logger.LogInformation(
            "AzureOpenAIChatService created with endpoint: {Endpoint}, deployment: {Deployment}, authMode: {AuthMode}",
            options.AzureOpenAIEndpoint,
            options.AzureOpenAIDeploymentName,
            options.AzureOpenAIAuthMode);
    }

    private static List<ChatMessage> ToMessages(IEnumerable<ChatTurn> turns)
    {
        var opts = new List<ChatMessage>();
        foreach (var t in turns)
        {
            var role = t.Role.ToLowerInvariant() switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User
            };
            opts.Add(new ChatMessage(role, t.Content));
        }
        return opts;
    }

    public async Task<string> CompleteAsync(IEnumerable<ChatTurn> turns, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turns, nameof(turns));
        _logger.LogInformation("[CompleteAsync] AzureOpenAI called with {TurnCount} turns", turns.Count());

        var messages = ToMessages(turns);
        var response = string.Empty;

        await foreach (var item in _chatClient.GetStreamingResponseAsync(messages, null, ct))
        {
            response += item.Text;
        }
        return response;
    }

    public async IAsyncEnumerable<string> StreamAsync(IEnumerable<ChatTurn> turns, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turns, nameof(turns));

        _logger.LogInformation("[StreamAsync] AzureOpenAI called with {TurnCount} turns", turns.Count());
        var messages = ToMessages(turns);

        await foreach (var item in _chatClient.GetStreamingResponseAsync(messages, null, ct))
        {
            yield return item.Text;
        }
    }
}
