// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.OpenAIChat;

[Collection("BaseTestCollection")]
public class AzureOpenAIChatServiceTest
{
    private readonly BaseTest _baseTest;
    private static readonly string[] Expected_When_StreamAsync_Then_YieldsPartsInOrder = ["You are a helpful assistant", "Hello", "Hi there"];

    public AzureOpenAIChatServiceTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        _baseTest.LogProvider.Logger.Clear();
    }

    #region Helpers

    private static IOptions<GptChatGrxmlConfiguration> CreateOptions() =>
        Options.Create(new GptChatGrxmlConfiguration
        {
            OpenAI_Provider = GptChatGrxmlConfiguration.OpenAIProvider_AzureOpenAI,
            AzureOpenAIEndpoint = "https://example-tests.openai.azure.com/",
            AzureOpenAIDeploymentName = "test-deployment",
            AzureOpenAIKey = "test-key"
        });

    private static IEnumerable<ChatTurn> CreateTurns() =>
        [
            new ChatTurn("system", "You are a helpful assistant"),
            new ChatTurn("user", "Hello"),
            new ChatTurn("assistant", "Hi there")
        ];

    private async IAsyncEnumerable<ChatResponseUpdate> GetMockChatUpdates(IEnumerable<ChatMessage> msgs)
    {
        foreach (var msg in msgs)
        {
            await Task.Delay(10); // simulate some delay
            yield return new ChatResponseUpdate(msg.Role, msg.Contents);
        }
    }

    private (AzureOpenAIChatService service,
             Mock<IAzureOpenAIClientFactory> factoryMock,
             Mock<IChatClient> chatClientMock) CreateService()
    {
        var chatClientMock = new Mock<IChatClient>();

        // We only need streaming behavior for current implementation
        chatClientMock
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> msgs, ChatOptions? opts, CancellationToken ct) =>
            GetMockChatUpdates(msgs));

        var factoryMock = new Mock<IAzureOpenAIClientFactory>();
        factoryMock
            .Setup(f => f.CreateChatClient(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns(chatClientMock.Object);

        var service = new AzureOpenAIChatService(CreateOptions(), factoryMock.Object);
        return (service, factoryMock, chatClientMock);
    }

    #endregion

    [Fact]
    public async Task When_CompleteAsync_Then_ConcatenatesAllStreamParts()
    {
        var (service, _, chatClientMock) = CreateService();
        var turns = CreateTurns();

        var result = await service.CompleteAsync(turns);

        Assert.Equal("You are a helpful assistantHelloHi there", result);
        chatClientMock.Verify(c => c.GetStreamingResponseAsync(
                It.Is<IEnumerable<ChatMessage>>(m => m.Count() == turns.Count()),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task When_StreamAsync_Then_YieldsPartsInOrder()
    {
        var (service, _, _) = CreateService();
        var turns = CreateTurns();

        var collected = new List<string>();
        await foreach (var piece in service.StreamAsync(turns))
        {
            collected.Add(piece);
        }

        Assert.Equal(Expected_When_StreamAsync_Then_YieldsPartsInOrder, collected);
    }

    [Fact]
    public async Task When_CompleteAsync_NullTurns_Then_ArgumentNullException()
    {
        var (service, _, _) = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CompleteAsync(null!));
    }

    [Fact]
    public async Task When_StreamAsync_NullTurns_Then_ArgumentNullException()
    {
        var (service, _, _) = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in service.StreamAsync(null!)) { }
        });
    }

    [Fact]
    public void When_Constructor_NullOptions_Then_ArgumentNullException()
    {
        var factory = new Mock<IAzureOpenAIClientFactory>();
        Assert.Throws<ArgumentNullException>(() => new AzureOpenAIChatService(null!, factory.Object));
    }

    [Fact]
    public void When_Constructor_NullFactory_Then_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AzureOpenAIChatService(CreateOptions(), null!));
    }

    [Fact]
    public async Task When_TurnRolesMapped_Then_SystemAssistantUserAssigned()
    {
        var (service, _, chatClientMock) = CreateService();

        var turns = new[]
        {
            new ChatTurn("system", "sys"),
            new ChatTurn("assistant", "asst"),
            new ChatTurn("user", "usr"),
            new ChatTurn("anythingElse", "fallback") // should map to user
        };

        await service.CompleteAsync(turns);

        chatClientMock.Verify(c => c.GetStreamingResponseAsync(
            It.Is<IEnumerable<ChatMessage>>(msgs =>
                msgs.Count() == 4 &&
                msgs.Any(m => m.Role.ToString() == "system" && m.Text == "sys") &&
                msgs.Any(m => m.Role.ToString() == "assistant" && m.Text == "asst") &&
                msgs.Count(m => m.Role.ToString() == "user") == 2),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
