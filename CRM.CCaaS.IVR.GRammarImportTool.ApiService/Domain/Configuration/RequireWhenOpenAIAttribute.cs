// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel.DataAnnotations;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;

public sealed class RequireWhenOpenAI : ValidationAttribute
{
    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (validationContext.ObjectInstance is GptChatGrxmlConfiguration gptChatGrxmlConfiguration)
        {
            if (gptChatGrxmlConfiguration.OpenAI_Provider == GptChatGrxmlConfiguration.OpenAIProvider_OpenAI
                && (value is null || string.IsNullOrWhiteSpace(value.ToString())))
            {
                return new ValidationResult($"{validationContext.MemberName} is required when OpenAI_Provider is set to OpenAI.");
            }
        }
#pragma warning disable CS8603 // Possible null reference return.
        return ValidationResult.Success;
#pragma warning restore CS8603 // Possible null reference return.
    }
}
