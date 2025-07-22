using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;

public interface IAzureOpenAIClientFactory
{
    IChatClient CreateChatClient(string? endpoint, string? deploymentName, string? apiKey);
}
