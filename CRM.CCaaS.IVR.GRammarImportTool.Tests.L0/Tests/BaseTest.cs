using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Common;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Grxml;
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
    public Mock<IConversionResultsStore> ResultsStoreMock { get; private set; } = new Mock<IConversionResultsStore>();
    public Mock<IBackgroundTaskQueue> QueueMock { get; private set; } = new Mock<IBackgroundTaskQueue>();

    public Mock<JobTracker> JobTrackerMock { get; private set; } = new Mock<JobTracker>();

    public Mock<FileValidator> FileValidatorMock { get => _fileValidatorMock; }
    public Mock<AiContentValidator> AiContentValidatorMock { get => _aiContentValidatorMock; }
    public Mock<TokenValidator> TokenValidatorMock { get => _tokenValidatorMock; }

    private bool _disposedValue;
    private readonly Mock<FileValidator> _fileValidatorMock;
    private readonly Mock<AiContentValidator> _aiContentValidatorMock;
    private readonly Mock<TokenValidator> _tokenValidatorMock;

    public BaseTest()
    {
        var logger = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(LogProvider);
        });
        ApiService.Util.Logging.GrITLoggerFactory.Instance = logger;

        _fileValidatorMock = new Mock<FileValidator>(Options.Create(GptChatGrxmlTestConfiguration));
        _aiContentValidatorMock = new Mock<AiContentValidator>(Options.Create(GptChatGrxmlTestConfiguration));
        _tokenValidatorMock = new Mock<TokenValidator>(Options.Create(GptChatGrxmlTestConfiguration));
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

        var resultData = new ConversionResult(new ConcurrentDictionary<string, string>());
        resultData.ResultData.TryAdd("file.yaml", "yaml: content");

        ResultsStoreMock.Setup(rs => rs.GetResultAsync("job123")).ReturnsAsync(resultData);
        ResultsStoreMock.Setup(rs => rs.GetResultAsync("jobResultIsNull")).ReturnsAsync(new ConversionResult(new ConcurrentDictionary<string, string>()));
        ResultsStoreMock.Setup(rs => rs.ResultExistsAsync("job123")).ReturnsAsync(true);
        ResultsStoreMock.Setup(rs => rs.RemoveResultAsync("job123")).Returns(Task.CompletedTask);
        ResultsStoreMock.Setup(rs => rs.UpdateResultAsync("job123", It.IsAny<ConversionResult>())).Returns(Task.CompletedTask);
        ResultsStoreMock.Setup(rs => rs.AddResultAsync(It.IsAny<string>(), It.IsAny<ConversionResult>())).Returns(Task.CompletedTask);

        QueueMock.Setup(q => q.EnqueueAsync(It.IsAny<JobTask>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        FileValidatorMock.Setup(fv => fv.ValidateUploadedFileAsync(It.IsAny<Microsoft.AspNetCore.Http.IFormFile>(), It.IsAny<bool>()))
            .ReturnsAsync(ValidationResult.Success());
        FileValidatorMock.Setup(fv => fv.ValidateUploadedFileAsync(It.Is<Microsoft.AspNetCore.Http.IFormFile>(f => f.FileName.Contains("bad.grxml")), It.IsAny<bool>()))
            .ReturnsAsync(ValidationResult.Failure("Invalid GRXML format in file."));
        FileValidatorMock.Setup(fv => fv.ValidateUploadedFileAsync(It.Is<Microsoft.AspNetCore.Http.IFormFile>(f => f.FileName.Contains("bad.zip")), It.IsAny<bool>()))
            .ReturnsAsync(ValidationResult.Failure("Invalid GRXML in the zip."));
        FileValidatorMock.Setup(fv => fv.ValidateXmlContentsAsync(It.IsAny<Stream>()))
            .ReturnsAsync(ValidationResult.Success());
        FileValidatorMock.Setup(fv => fv.ValidateZipContentsAsync(It.IsAny<Stream>()))
            .ReturnsAsync(ValidationResult.Success());

        AiContentValidatorMock.Setup(fv => fv.ValidateAiPromptAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(ValidationResult.Success);
        AiContentValidatorMock.Setup(fv => fv.ValidateAiPromptAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("badGrxml", StringComparison.OrdinalIgnoreCase))))
            .ReturnsAsync(ValidationResult.Failure("Error validating AI prompt content"));

        TokenValidatorMock.Setup(tv => tv.ValidateTokenCount(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ValidationResult.Success());
        TokenValidatorMock.Setup(tv => tv.MaxTokenLimit)
            .Returns(10000);

        services.AddSingleton<IHostEnvironment>(new UnitTestHostEnvironment { EnvironmentName = "Development" });
        services.AddKeyedSingleton(InMemoryConversionResultsStore.SERVICE_KEY, ResultsStoreMock.Object);
        services.AddSingleton(QueueMock.Object);
        services.AddSingleton(provider => AzureOpenAIClientFactoryMock.Object);
        services.AddSingleton(provider =>
        {
            var config = provider.GetRequiredService<IOptions<GptChatGrxmlConfiguration>>();
            var azureFactory = provider.GetRequiredService<IAzureOpenAIClientFactory>();
            return ChatServiceFactory.Create(config, azureFactory);
        });
        services.AddKeyedTransient<IGptChat, GptChatGrxmlToMcsConverter>(GptChatGrxmlToMcsConverter.SERVICE_KEY);
        services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        services.AddControllers().AddApplicationPart(typeof(GrITController).Assembly);

        GptChatGrxmlTestConfiguration.AzureOpenAIEndpoint = "https://test.openai.azure.com/";
        GptChatGrxmlTestConfiguration.AzureOpenAIDeploymentName = "test-deployment";
        GptChatGrxmlTestConfiguration.AzureOpenAIKey = "test-key";
        GptChatGrxmlTestConfiguration.MaxAllowedConversionTimeSingleFileSec = 5;
        GptChatGrxmlTestConfiguration.MaxAllowedConversionTimeTotalSec = 10;
        GptChatGrxmlTestConfiguration.AllowedUploadFileSizeRangeBytes = 50;
        GptChatGrxmlTestConfiguration.DegreeParallelism = 2;
        GptChatGrxmlTestConfiguration.BackgroundTasksQueueCapacity = 2;
        GptChatGrxmlTestConfiguration.InitialChatHistory = new List<GPTMessage>
        {
            new("user", "You are a helpful assistant that converts GRXML files to MCS format.")
        };
        GptChatGrxmlTestConfiguration.RetryDelaySec = 1;
        services.AddSingleton(Options.Create(GptChatGrxmlTestConfiguration));

        services.AddTransient(provider => FileValidatorMock.Object);
        services.AddTransient(provider => AiContentValidatorMock.Object);
        services.AddTransient(provider => TokenValidatorMock.Object);

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
