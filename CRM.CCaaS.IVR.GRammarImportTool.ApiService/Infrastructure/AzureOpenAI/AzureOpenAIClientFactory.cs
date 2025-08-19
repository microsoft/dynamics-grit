using Azure;
using Azure.AI.OpenAI;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.AzureOpenAI;

public class AzureOpenAIClientFactory : IAzureOpenAIClientFactory
{
    private readonly ILogger<AzureOpenAIClientFactory> _logger = GrITLoggerFactory.CreateLogger<AzureOpenAIClientFactory>();
    /// <summary>
    /// Creates the chat client for OpenAI.
    /// </summary>
    public IChatClient CreateChatClient(string? endpoint, string? deployment, string? key)
    {

        if (string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(deployment)
            || string.IsNullOrWhiteSpace(key))
        {
            _logger.LogError("One of the Azure OpenAI configuration parameters is missing or contains only whitespace.");
            throw new ArgumentException("One of the Azure OpenAI configuration parameters is missing or contains only whitespace.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uriResult))
        {
            _logger.LogError("The provided endpoint is not a valid URI.");
            throw new ArgumentException("The provided endpoint is not a valid URI.");
        }

        _logger.LogInformation("[CreateChatClient]: Successfully created chat client for deployment {Deployment}.", deployment);
        return new AzureOpenAIClient(uriResult, new AzureKeyCredential(key))
            .AsChatClient(deployment);
    }
}
