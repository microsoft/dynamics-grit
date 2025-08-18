using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Configuration;

public class GptChatGrxmlConfigurationTest : IClassFixture<BaseTest>, IDisposable
{
    private bool _disposedValue;

    public GptChatGrxmlConfigurationTest()
    {

    }

    [Fact]
    public void When_DefaultConfiguration_Then_PropertiesHaveExpectedDefaults()
    {
        var config = new GptChatGrxmlConfiguration();

        Assert.Equal(7, config.DegreeParallelism);
        Assert.Equal(3, config.MaxRetries);
        Assert.Equal(10, config.RetryDelaySec);
        Assert.Equal(600, config.MaxAllowedConversionTimeSingleFileSec);
        Assert.Equal(3600, config.MaxAllowedConversionTimeTotalSec);
        Assert.Equal(10 * GptChatGrxmlConfiguration.OneMbInBytes, config.AllowedUploadFileSizeRangeBytes);
        Assert.Equal(5 * GptChatGrxmlConfiguration.OneMbInBytes, config.MaxSignalRMessageSizeBytes);
        Assert.Equal(100, config.ResultStreamChannelCapacity);
        Assert.NotNull(config.InitialChatHistory);
        Assert.Empty(config.InitialChatHistory);
        Assert.Equal(string.Empty, config.AzureOpenAIEndpoint);
        Assert.Equal(string.Empty, config.AzureOpenAIDeploymentName);
        Assert.Equal(string.Empty, config.AzureOpenAIKey);
        Assert.Equal(string.Empty, config.DisclaimerAI);
    }

    [Fact]
    public void When_ValidConfiguration_Then_ValidationSucceeds()
    {
        var config = new GptChatGrxmlConfiguration
        {
            DegreeParallelism = 10,
            MaxRetries = 5,
            RetryDelaySec = 20,
            MaxAllowedConversionTimeSingleFileSec = 5,
            MaxAllowedConversionTimeTotalSec = 100,
            AllowedUploadFileSizeRangeBytes = GptChatGrxmlConfiguration.OneMbInBytes,
            MaxSignalRMessageSizeBytes = GptChatGrxmlConfiguration.OneMbInBytes,
            ResultStreamChannelCapacity = 200,
            InitialChatHistory = new List<GPTMessage>
            {
                new("user", "test")
            },
            AzureOpenAIEndpoint = "https://test.openai.azure.com/",
            AzureOpenAIDeploymentName = "deployment",
            AzureOpenAIKey = "key",
            DisclaimerAI = "disclaimer"
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), results, true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void When_MissingRequiredProperties_Then_ValidationFails()
    {
        var config = new GptChatGrxmlConfiguration
        {
            InitialChatHistory = null,
            AzureOpenAIEndpoint = "",
            AzureOpenAIDeploymentName = "",
            AzureOpenAIKey = ""
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), results, true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.ErrorMessage?.Contains("Initial Chat history is required", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(results, r => r.ErrorMessage?.Contains("Azure OpenAI endpoint is required", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(results, r => r.ErrorMessage?.Contains("Azure OpenAI deployment name is required", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(results, r => r.ErrorMessage?.Contains("Azure OpenAI key is required", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void When_GPTMessageMissingRoleOrContent_Then_ValidationFails()
    {
        var message = new GPTMessage("", "");
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(message, new ValidationContext(message), results, true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.ErrorMessage?.Contains("Role is required", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(results, r => r.ErrorMessage?.Contains("Content is required", StringComparison.OrdinalIgnoreCase) == true);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~GritTest()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
