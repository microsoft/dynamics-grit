// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.OpenAIChat;

[Collection("BaseTestCollection")]
public class ChatServiceFactoryTest
{
    private readonly BaseTest _baseTest;

    public ChatServiceFactoryTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        _baseTest.LogProvider.Logger.Clear();
    }

    private static IOptions<GptChatGrxmlConfiguration> CreateOptions(string provider) =>
        Options.Create(new GptChatGrxmlConfiguration
        {
            OpenAI_Provider = provider,
            // Provide minimal other required fields if constructors later depend on them
            AzureOpenAIEndpoint = "https://example.openai.azure.com/",
            AzureOpenAIDeploymentName = "deployment",
            AzureOpenAIKey = "key",
            OpenAI_ApiKey = "apiKey",
            OpenAI_Model = "gpt-model"
        });

    [Fact]
    public void When_ProviderIsAzureOpenAI_Then_ReturnsAzureOpenAIChatService()
    {
        var opts = CreateOptions(GptChatGrxmlConfiguration.OpenAIProvider_AzureOpenAI);
        var azureFactory = new Mock<IAzureOpenAIClientFactory>();

        var service = ChatServiceFactory.Create(opts, azureFactory.Object);

        Assert.NotNull(service);
        Assert.IsType<AzureOpenAIChatService>(service);
    }

    [Fact]
    public void When_ProviderIsOpenAI_Then_ReturnsOpenAIChatService()
    {
        var opts = CreateOptions(GptChatGrxmlConfiguration.OpenAIProvider_OpenAI);
        var azureFactory = new Mock<IAzureOpenAIClientFactory>();

        var service = ChatServiceFactory.Create(opts, azureFactory.Object);

        Assert.NotNull(service);
        Assert.IsType<OpenAIChatService>(service);
    }

    [Fact]
    public void When_ProviderUnknown_Then_NotSupportedException()
    {
        var opts = CreateOptions("SomeOtherProvider");
        var azureFactory = new Mock<IAzureOpenAIClientFactory>();

        var ex = Assert.Throws<NotSupportedException>(() =>
            ChatServiceFactory.Create(opts, azureFactory.Object));

        Assert.Contains("SomeOtherProvider", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void When_OptionsIsNull_Then_NotSupportedException()
    {
        IOptions<GptChatGrxmlConfiguration>? opts = null;
        var azureFactory = new Mock<IAzureOpenAIClientFactory>();

        var ex = Assert.Throws<NotSupportedException>(() =>
            ChatServiceFactory.Create(opts!, azureFactory.Object));

        Assert.Contains("Unknown provider", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void When_ProviderNullInConfig_Then_NotSupportedException()
    {
        var cfg = new GptChatGrxmlConfiguration
        {
            OpenAI_Provider = null!,
            AzureOpenAIEndpoint = "https://example",
            AzureOpenAIDeploymentName = "dep",
            AzureOpenAIKey = "key",
            OpenAI_ApiKey = "api",
            OpenAI_Model = "model"
        };
        var azureFactory = new Mock<IAzureOpenAIClientFactory>();

        var ex = Assert.Throws<NotSupportedException>(() =>
            ChatServiceFactory.Create(Options.Create(cfg), azureFactory.Object));

        Assert.Contains("Unknown provider", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
