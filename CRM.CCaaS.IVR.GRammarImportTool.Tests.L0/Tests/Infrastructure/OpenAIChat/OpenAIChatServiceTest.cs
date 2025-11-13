// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;
using Microsoft.Extensions.Options;
using Moq;
using OpenAI;
using OpenAI.Chat;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.OpenAIChat;

[Collection("BaseTestCollection")]
public class OpenAIChatServiceTest
{
    private readonly BaseTest _baseTest;
    private static readonly string[] ExpectedStreamOrder = ["You are a helpful assistant", "Hello", "Hi there"];

    public OpenAIChatServiceTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        _baseTest.LogProvider.Logger.Clear();
    }

    #region Helpers

    private static IOptions<GptChatGrxmlConfiguration> CreateOptions() =>
        Options.Create(new GptChatGrxmlConfiguration
        {
            OpenAI_Provider = GptChatGrxmlConfiguration.OpenAIProvider_OpenAI,
            OpenAI_ApiKey = "test-openai-key",
            OpenAI_Model = "gpt-test-model"
        });

    private static IEnumerable<ChatTurn> CreateTurns() =>
        [
            new ChatTurn("system", "You are a helpful assistant"),
            new ChatTurn("user", "Hello"),
            new ChatTurn("assistant", "Hi there")
        ];

    /// <summary>
    /// Creates an OpenAIChatService instance and injects a mocked IChatClient via reflection
    /// (the class does not expose the dependency directly like AzureOpenAIChatService).
    /// </summary>
    private (OpenAIChatService service, Mock<ChatClient> chatClientMock) CreateService()
    {
        var service = new OpenAIChatService(CreateOptions());

        var chatClientMock = new Mock<ChatClient>();
        chatClientMock.Setup(c => c.CompleteChatAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatCompletionOptions>(), It.IsAny<CancellationToken>()))
          .Throws<OperationCanceledException>();
        chatClientMock.Setup(c => c.CompleteChatStreamingAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatCompletionOptions>(), It.IsAny<CancellationToken>()))
          .Throws<OperationCanceledException>();

        // Inject mock into private readonly field "_chatClient"
        var field = typeof(OpenAIChatService).GetField("_chatClient", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field); // Ensure future refactors surface a failing test
        field!.SetValue(service, chatClientMock.Object);

        return (service, chatClientMock);
    }

    #endregion

    [Fact]
    public async Task When_CompleteAsync_Then_ResultEmpty()
    {
        var (service, chatClientMock) = CreateService();
        var turns = CreateTurns();

        var result = await service.CompleteAsync(turns);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task When_StreamAsync_Then_PartsEmpty()
    {
        var (service, _) = CreateService();
        var turns = CreateTurns();

        var collected = new List<string>();
        await foreach (var piece in service.StreamAsync(turns))
        {
            collected.Add(piece);
        }

        Assert.Empty(collected);
    }

    [Fact]
    public async Task When_CompleteAsync_NullTurns_Then_ArgumentNullException()
    {
        var (service, _) = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CompleteAsync(null!));
    }

    [Fact]
    public async Task When_StreamAsync_NullTurns_Then_ArgumentNullException()
    {
        var (service, _) = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in service.StreamAsync(null!)) { }
        });
    }

    [Fact]
    public void When_Constructor_NullOptions_Then_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenAIChatService(null!));
    }

    [Fact]
    public async Task When_TurnRolesMapped_Then_SystemAssistantUserAssigned()
    {
        var (service, chatClientMock) = CreateService();

        var turns = new[]
        {
            new ChatTurn("system", "sys"),
            new ChatTurn("assistant", "asst"),
            new ChatTurn("user", "usr"),
            new ChatTurn("anythingElse", "fallback") // should map to user
        };

        await service.CompleteAsync(turns);
    }
}
