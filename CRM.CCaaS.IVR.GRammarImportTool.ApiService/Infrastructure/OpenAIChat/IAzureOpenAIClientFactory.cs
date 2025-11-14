// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;

public interface IAzureOpenAIClientFactory
{
    IChatClient CreateChatClient(string? endpoint, string? deploymentName, string? apiKey);
}
