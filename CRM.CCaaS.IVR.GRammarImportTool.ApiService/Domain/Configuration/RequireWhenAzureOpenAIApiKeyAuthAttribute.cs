// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.ComponentModel.DataAnnotations;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;

/// <summary>
/// Validation attribute that requires the decorated property only when
/// <see cref="GptChatGrxmlConfiguration.OpenAI_Provider"/> is set to
/// <c>AzureOpenAI</c> AND <see cref="GptChatGrxmlConfiguration.AzureOpenAIAuthMode"/>
/// is set to <c>ApiKey</c>. Use on credentials that are only meaningful for
/// the static-key authentication path (so ManagedIdentity / DefaultAzureCredential
/// configurations are not blocked by missing API-key fields).
/// </summary>
public sealed class RequireWhenAzureOpenAIApiKeyAuth : ValidationAttribute
{
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (validationContext.ObjectInstance is GptChatGrxmlConfiguration gptChatGrxmlConfiguration)
        {
            var isAzureProvider = gptChatGrxmlConfiguration.OpenAI_Provider == GptChatGrxmlConfiguration.OpenAIProvider_AzureOpenAI;
            var isApiKeyAuth = string.Equals(
                gptChatGrxmlConfiguration.AzureOpenAIAuthMode,
                GptChatGrxmlConfiguration.AzureOpenAIAuthMode_ApiKey,
                StringComparison.OrdinalIgnoreCase);

            if (isAzureProvider
                && isApiKeyAuth
                && (value is null || string.IsNullOrWhiteSpace(value.ToString())))
            {
                return new ValidationResult($"{validationContext.MemberName} is required when AzureOpenAI_Provider is set to AzureOpenAI.");
            }
        }
#pragma warning disable CS8603 // Possible null reference return.
        return ValidationResult.Success;
#pragma warning restore CS8603 // Possible null reference return.
    }
}
