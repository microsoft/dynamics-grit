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
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;
using static System.Net.Mime.MediaTypeNames;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0;
public class BaseTest : IDisposable
{
    public TestLoggerProvider LogProvider { get; private set; } = new TestLoggerProvider();
    public IServiceProvider? ServiceProvider { get; private set; }
    public GptChatGrxmlConfiguration GptChatGrxmlTestConfiguration { get; private set; } = new GptChatGrxmlConfiguration();

    public Mock<IChatClient> ChatClientMock { get; private set; } = new Mock<IChatClient>();

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
        BuildServiceProvider(services);

        ChatClientMock
            .Setup(x => x.GetStreamingResponseAsync(It.IsAny<List<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(CreateGptResult);
    }

    protected async IAsyncEnumerable<ChatResponseUpdate> CreateGptResult()
    {
        var enumerable = new List<ChatResponseUpdate> { new ChatResponseUpdate(ChatRole.Assistant, "hello: result") };
        foreach (var item in enumerable)
        {
            yield return await Task.FromResult(item);
        }
    }

    protected void BuildServiceProvider(ServiceCollection services)
    {
        ServiceProvider = services.BuildServiceProvider();
    }

    protected ServiceCollection BuildMocks()
    {
        var hostingEnvironment = new HostingEnvironment
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Development"
        };

        var services = new ServiceCollection();
        services.AddKeyedTransient<IGptChat, GptChatGrxmlToMcsConverter>(GptChatGrxmlToMcsConverter.SERVICE_KEY);
        services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        services.AddControllers().AddApplicationPart(typeof(GrITController).Assembly);

        var testConfiguration = new GptChatGrxmlConfiguration();
        testConfiguration.AzureOpenAIEndpoint = "https://test.openai.azure.com/";
        testConfiguration.AzureOpenAIDeploymentName = "test-deployment";
        testConfiguration.AzureOpenAIKey = "test-key";
        services.AddSingleton(Options.Create(testConfiguration));

        services.AddLogging(builder =>
        {
            builder.AddProvider(LogProvider);
        });

        return services;
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                LogProvider.Dispose(); // Dispose the LogProvider
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
