// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;

public class AzureOpenAIClientFactory : IAzureOpenAIClientFactory
{
    private readonly ILogger<AzureOpenAIClientFactory> _logger = GrITLoggerFactory.CreateLogger<AzureOpenAIClientFactory>();

    /// <summary>
    /// Legacy API-key-only path used by tests and any caller that still passes
    /// raw strings. Behaviour preserved from the original implementation.
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

    /// <summary>
    /// New entry point — honours <see cref="GptChatGrxmlConfiguration.AzureOpenAIAuthMode"/>.
    /// </summary>
    public IChatClient CreateChatClient(GptChatGrxmlConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.AzureOpenAIEndpoint)
            || string.IsNullOrWhiteSpace(configuration.AzureOpenAIDeploymentName))
        {
            _logger.LogError("Azure OpenAI endpoint or deployment name is missing or contains only whitespace.");
            throw new ArgumentException("Azure OpenAI endpoint and deployment name must both be set.");
        }

        if (!Uri.TryCreate(configuration.AzureOpenAIEndpoint, UriKind.Absolute, out var uriResult))
        {
            _logger.LogError("The provided endpoint is not a valid URI.");
            throw new ArgumentException("The provided endpoint is not a valid URI.");
        }

        var authMode = configuration.AzureOpenAIAuthMode ?? GptChatGrxmlConfiguration.AzureOpenAIAuthMode_ApiKey;

        // Safety fallback: when the configured mode is ApiKey but no key is
        // present, promote to DefaultAzureCredential instead of failing or
        // silently sending an empty key. This is the "secure by default when
        // no key is provided" behaviour referenced in docs/security-posture.md.
        if (string.Equals(authMode, GptChatGrxmlConfiguration.AzureOpenAIAuthMode_ApiKey, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(configuration.AzureOpenAIKey))
        {
            _logger.LogWarning(
                "[CreateChatClient]: AzureOpenAIAuthMode=ApiKey but no AzureOpenAIKey was provided. Falling back to DefaultAzureCredential.");
            authMode = GptChatGrxmlConfiguration.AzureOpenAIAuthMode_DefaultAzureCredential;
        }

        AzureOpenAIClient azureClient;
        if (string.Equals(authMode, GptChatGrxmlConfiguration.AzureOpenAIAuthMode_ApiKey, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[CreateChatClient]: Authenticating with AzureKeyCredential for deployment {Deployment}.", configuration.AzureOpenAIDeploymentName);
            azureClient = new AzureOpenAIClient(uriResult, new AzureKeyCredential(configuration.AzureOpenAIKey));
        }
        else
        {
            TokenCredential credential = BuildTokenCredential(authMode, configuration.AzureOpenAIManagedIdentityClientId);
            _logger.LogInformation(
                "[CreateChatClient]: Authenticating with {AuthMode} for deployment {Deployment}.",
                authMode,
                configuration.AzureOpenAIDeploymentName);
            azureClient = new AzureOpenAIClient(uriResult, credential);
        }

        return azureClient.AsChatClient(configuration.AzureOpenAIDeploymentName);
    }

    private TokenCredential BuildTokenCredential(string authMode, string? managedIdentityClientId)
    {
        if (string.Equals(authMode, GptChatGrxmlConfiguration.AzureOpenAIAuthMode_ManagedIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(managedIdentityClientId)
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(managedIdentityClientId);
        }

        if (string.Equals(authMode, GptChatGrxmlConfiguration.AzureOpenAIAuthMode_DefaultAzureCredential, StringComparison.OrdinalIgnoreCase))
        {
            return new DefaultAzureCredential();
        }

        _logger.LogError("Unsupported AzureOpenAIAuthMode value: {AuthMode}.", authMode);
        throw new ArgumentException($"Unsupported AzureOpenAIAuthMode value: '{authMode}'. Expected one of: ApiKey, ManagedIdentity, DefaultAzureCredential.");
    }
}
