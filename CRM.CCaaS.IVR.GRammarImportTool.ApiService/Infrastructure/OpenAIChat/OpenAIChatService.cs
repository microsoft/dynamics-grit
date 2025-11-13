// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.ClientModel;
using System.Data;
using System.Runtime.CompilerServices;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;

public sealed class OpenAIChatService : IChatService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<OpenAIChatService> _logger;

    public OpenAIChatService(IOptions<GptChatGrxmlConfiguration> gptChatConfiguration)
    {
        ArgumentNullException.ThrowIfNull(gptChatConfiguration?.Value, nameof(gptChatConfiguration));

        _logger = GrITLoggerFactory.CreateLogger<OpenAIChatService>();

        var options = gptChatConfiguration.Value;
        if (string.IsNullOrEmpty(options.OpenAI_ApiKey)
            || string.IsNullOrEmpty(options.OpenAI_Model))
        {
            throw new ArgumentException("One of the OpenAI configuration parameters is missing or contains only whitespace.");
        }
        _chatClient = new ChatClient(options.OpenAI_Model, options.OpenAI_ApiKey);
    }

    private static List<ChatMessage> ToMessages(IEnumerable<ChatTurn> turns)
    {
        var list = new List<ChatMessage>();
        foreach (var t in turns)
        {
            switch (t.Role.ToLowerInvariant())
            {
                case "system":
                    list.Add(new SystemChatMessage(t.Content));
                    break;
                case "assistant":
                    list.Add(new AssistantChatMessage(t.Content));
                    break;
                default:
                    list.Add(new UserChatMessage(t.Content));
                    break;
            }
        }
        return list;
    }

    public async Task<string> CompleteAsync(IEnumerable<ChatTurn> turns, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turns, nameof(turns));

        _logger.LogInformation("[CompleteAsync] OpenAI called with {TurnCount} turns", turns.Count());
        var messages = ToMessages(turns);
        try
        {
            ct.ThrowIfCancellationRequested();
            ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, cancellationToken: ct);
            // v2.4.x: content is a list; pick the first text part
            return completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(IEnumerable<ChatTurn> turns, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turns, nameof(turns));

        _logger.LogInformation("[StreamAsync] OpenAI called with {TurnCount} turns", turns.Count());
        var messages = ToMessages(turns);
        AsyncCollectionResult<StreamingChatCompletionUpdate>? updates;
        try
        {
            ct.ThrowIfCancellationRequested();
            updates = _chatClient.CompleteChatStreamingAsync(messages, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }

        await foreach (var update in updates.WithCancellation(ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return part.Text;
            }
        }
    }
}
