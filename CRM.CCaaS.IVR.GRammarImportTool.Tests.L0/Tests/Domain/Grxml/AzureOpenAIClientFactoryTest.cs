using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Grxml;
[Collection("BaseTestOpenAICollection")]
public class AzureOpenAIClientFactoryTest
{
    private readonly BaseTestAzureOpenAIClientFactory _baseTest;
    private readonly IAzureOpenAIClientFactory _azureOpenAIClientFactory;

    public AzureOpenAIClientFactoryTest(BaseTestAzureOpenAIClientFactory baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        _azureOpenAIClientFactory = _baseTest.ServiceProvider.GetRequiredService<IAzureOpenAIClientFactory>();
    }

    [Theory]
    [InlineData(null, "test-deployment", "test-key")]
    [InlineData("https://test.openai.azure.com/", null, "test-key")]
    [InlineData("https://test.openai.azure.com/", "test-deployment", null)]
    public void When_CreateChatClient_InvalidParameters_Then_ThrowsArgumentNullException(string? endpoint, string? deployment, string? key)
    {
        Assert.Throws<ArgumentException>(() =>
            _azureOpenAIClientFactory.CreateChatClient(endpoint, deployment, key));
    }

    [Fact]
    public void When_CreateChatClient_ValidParameters_Then_ReturnsChatClient()
    {
        var endpoint = "https://test.openai.azure.com/";
        var deployment = "test-deployment";
        var key = "test-key";

        var chatClient = _azureOpenAIClientFactory.CreateChatClient(endpoint, deployment, key);

        Assert.NotNull(chatClient);
        Assert.IsAssignableFrom<IChatClient>(chatClient);
    }
}
