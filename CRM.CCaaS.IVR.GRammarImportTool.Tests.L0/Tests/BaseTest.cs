using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;
using static System.Net.Mime.MediaTypeNames;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests;
public class BaseTest : IDisposable
{
    public TestLoggerProvider LogProvider { get; private set; } = new TestLoggerProvider();
    public IServiceProvider? ServiceProvider { get; private set; }
    public GptChatGrxmlConfiguration GptChatGrxmlTestConfiguration { get; private set; } = new GptChatGrxmlConfiguration();

    public Mock<IChatClient> ChatClientMock { get; private set; } = new Mock<IChatClient>();
    public Mock<IAzureOpenAIClientFactory> AzureOpenAIClientFactoryMock { get; private set; } = new Mock<IAzureOpenAIClientFactory>();

    private bool _disposedValue;

    public BaseTest()
    {
        var logger = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(LogProvider);
        });
        ApiService.Util.Logging.GrITLoggerFactory.Instance = logger;

        SetupMocks();
    }

    protected virtual void SetupMocks()
    {
        var services = BuildMocks();
        SetupChatClientMock();
        BuildServiceProvider(services);
    }

    public void SetupChatClientMock()
    {
        ChatClientMock
            .Setup(x => x.GetStreamingResponseAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> chatHistory, ChatOptions? options, CancellationToken token) =>
            {
                if (chatHistory.Any(y => y.Text.Contains("clientException.grxml", StringComparison.OrdinalIgnoreCase)))
                    return CreateErrorGptResult("clientException.grxml");

                if (chatHistory.Any(y => y.Text.Contains("badYaml.grxml", StringComparison.OrdinalIgnoreCase)))
                    return CreateBadGptResult();

                if (chatHistory.Any(y => y.Text.Contains("unKnownException.grxml", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("Unknown exception occurred during processing.");

                if (chatHistory.Any(y => y.Text.Contains("timeout5000", StringComparison.OrdinalIgnoreCase)))
                {
                    Thread.Sleep(5000); // Simulate a timeout
                    throw new OperationCanceledException("Operation timed out.");
                }

                if (chatHistory.Any(y => y.Text.Contains("timeout4000", StringComparison.OrdinalIgnoreCase)))
                {
                    Thread.Sleep(4000); // Simulate a delay
                    return CreateGptResult();
                }

                return CreateGptResult();
            });

        AzureOpenAIClientFactoryMock
            .Setup(x => x.CreateChatClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ChatClientMock.Object);
    }

    protected void BuildServiceProvider(ServiceCollection services)
    {
        ServiceProvider = services.BuildServiceProvider();
    }

    protected ServiceCollection BuildMocks()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new UnitTestHostEnvironment { EnvironmentName = "Development" });
        services.AddKeyedTransient<IGptChat, GptChatGrxmlToMcsConverter>(GptChatGrxmlToMcsConverter.SERVICE_KEY);
        services.AddTransient(provider => AzureOpenAIClientFactoryMock.Object);
        services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        services.AddControllers().AddApplicationPart(typeof(GrITController).Assembly);

        var testConfiguration = new GptChatGrxmlConfiguration();
        testConfiguration.AzureOpenAIEndpoint = "https://test.openai.azure.com/";
        testConfiguration.AzureOpenAIDeploymentName = "test-deployment";
        testConfiguration.AzureOpenAIKey = "test-key";
        testConfiguration.MaxAllowedConversionTimeSingleFileSec = 5;
        testConfiguration.MaxAllowedConversionTimeTotalSec = 10;
        testConfiguration.DegreeParallelism = 2;
        testConfiguration.InitialChatHistory = new List<GPTMessage>
        {
            new GPTMessage
            {
                Role = ChatRole.User.ToString(),
                Content = "You are a helpful assistant that converts GRXML files to MCS format."
            }
        };
        testConfiguration.RetryDelaySec = 1;
        services.AddSingleton(Options.Create(testConfiguration));

        services.AddLogging(builder =>
        {
            builder.AddProvider(LogProvider);
        });

        return services;
    }

    protected async IAsyncEnumerable<ChatResponseUpdate> CreateGptResult()
    {
        var enumerable = new List<ChatResponseUpdate> { new ChatResponseUpdate(ChatRole.Assistant, "hello: result") };
        foreach (var item in enumerable)
        {
            yield return await Task.FromResult(item);
        }
    }

    protected async IAsyncEnumerable<ChatResponseUpdate> CreateBadGptResult()
    {
        var enumerable = new List<ChatResponseUpdate> { new ChatResponseUpdate(ChatRole.Assistant,
            @"   hello:
this: is bad yaml") };
        foreach (var item in enumerable)
        {
            yield return await Task.FromResult(item);
        }
    }

    protected async IAsyncEnumerable<ChatResponseUpdate> CreateErrorGptResult(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName, nameof(fileName));

        if (fileName.Equals("clientException.grxml", StringComparison.OrdinalIgnoreCase))
            throw new System.ClientModel.ClientResultException("Bad GPT response");

        var enumerable = new List<ChatResponseUpdate> { new ChatResponseUpdate(ChatRole.Assistant, "") };
        foreach (var item in enumerable)
        {
            yield return await Task.FromResult(item);
        }
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                LogProvider.Dispose(); // Dispose the LogProvider

                if (ServiceProvider is IDisposable disposable)
                    disposable.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~BaseUnitTest()
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
