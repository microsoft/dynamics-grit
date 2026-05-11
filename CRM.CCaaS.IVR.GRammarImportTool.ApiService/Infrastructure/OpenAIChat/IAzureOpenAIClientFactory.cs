// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;

public interface IAzureOpenAIClientFactory
{
    /// <summary>
    /// Legacy entry point — always uses an <c>AzureKeyCredential</c>. Kept for
    /// existing callers and tests. New code should call
    /// <see cref="CreateChatClient(GptChatGrxmlConfiguration)"/> so that the
    /// configured authentication mode (ApiKey / ManagedIdentity /
    /// DefaultAzureCredential) is honoured.
    /// </summary>
    IChatClient CreateChatClient(string? endpoint, string? deploymentName, string? apiKey);

    /// <summary>
    /// Creates a chat client using the auth mode configured in
    /// <paramref name="configuration"/>: API key, managed identity, or
    /// <c>DefaultAzureCredential</c>. When <c>AzureOpenAIAuthMode = ApiKey</c>
    /// but no key is provided, the factory falls back to
    /// <c>DefaultAzureCredential</c> rather than failing — so a missing key
    /// never lands as silent insecure behaviour.
    /// </summary>
    IChatClient CreateChatClient(GptChatGrxmlConfiguration configuration);
}
