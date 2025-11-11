using System;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;

public static class ChatServiceFactory
{
    public static IChatService Create(IOptions<GptChatGrxmlConfiguration> gptChatConfiguration, IAzureOpenAIClientFactory azureOpenAIClientFactory)
    {
        var provider = gptChatConfiguration?.Value?.OpenAI_Provider;
        return provider switch
        {
            GptChatGrxmlConfiguration.OpenAIProvider_AzureOpenAI => new AzureOpenAIChatService(gptChatConfiguration!, azureOpenAIClientFactory),
            GptChatGrxmlConfiguration.OpenAIProvider_OpenAI => new OpenAIChatService(gptChatConfiguration!),   // v2.4.x
            _ => throw new NotSupportedException($"Unknown provider: {provider}")
        };
    }
}
