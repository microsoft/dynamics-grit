using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Hubs;

public class GrITHubTest : IClassFixture<BaseTest>, IDisposable
{
    static readonly byte[] TEST_XML_STRING = Encoding.UTF8.GetBytes("<test>test</test>");
    static readonly string TEST_CONNECTION_ID = "test-connection-id";
    static readonly string HASHED_TEST_CONNECTION_ID = "b1a13b7481098984f0d29028e5de3f07a57d25746e6d67a3a9606b46e006c516";

    private readonly Mock<IHubCallerClients> _mockClients = new Mock<IHubCallerClients>();
    private readonly Mock<ISingleClientProxy> _mockCaller = new Mock<ISingleClientProxy>();
    private readonly Mock<HubCallerContext> _mockContext = new Mock<HubCallerContext>();
    private readonly Mock<IGptChat> _gptChatMock = new();
    private readonly Mock<IOptions<GptChatGrxmlConfiguration>> _optionsMock = new();

    private readonly GptChatGrxmlConfiguration _config = new()
    {
        MaxAllowedConversionTimeSingleFileSec = 1,
        ResultStreamChannelCapacity = 10
    };

    private readonly GrITHub _hub;

    private bool _disposedValue;
    private readonly BaseTest _baseTest;
    private static readonly byte[] EmptyZipBytes = [];

    public GrITHubTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        _optionsMock.Setup(x => x.Value).Returns(_config);

        _mockContext.SetupGet(c => c.ConnectionId).Returns(TEST_CONNECTION_ID);
        _mockClients.Setup(c => c.Caller).Returns(_mockCaller.Object);

        _hub = new GrITHub(_gptChatMock.Object, _optionsMock.Object)
        {
            Context = _mockContext.Object,
            Clients = _mockClients.Object
        };

        _baseTest.LogProvider.Logger.Clear();
    }

    [Fact]
    public async Task OnConnectedAsync_WhenCalled_Then_ConnectionIdLogged()
    {
        await _hub.OnConnectedAsync();

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains($"Client connected. ConnectionId={HASHED_TEST_CONNECTION_ID}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GrxmlZipConvert_When_EmptyZip_Then_ErrorLogged()
    {
        await _hub.GrxmlZipConvert(EmptyZipBytes);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains($"[GrxmlZipConvert] Called with null or empty zipBytes. ConnectionId={HASHED_TEST_CONNECTION_ID}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GrxmlZipConvert_When_ValidBytes_Then_ProgressResultsLogged()
    {
        _gptChatMock.Setup(x => x.ConvertZipAsync(
            It.IsAny<Stream>(),
            It.IsAny<Func<int, string, Task>>(),
            It.IsAny<Func<byte[], Task>>()))
            .Returns(async (Stream s, Func<int, string, Task> progress, Func<byte[], Task> completed) =>
            {
                await progress(50, "50%");
                await completed(Encoding.UTF8.GetBytes("yaml: result"));
                return new MemoryStream();
            });

        await _hub.GrxmlZipConvert(TEST_XML_STRING);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("Progress=50, Message=50%", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logMessages, m => m.Contains($"Progress update. ConnectionId={HASHED_TEST_CONNECTION_ID}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GrxmlZipConvert_When_Exception_Then_ErrorLogged()
    {
        _gptChatMock.Setup(x => x.ConvertZipAsync(
            It.IsAny<Stream>(),
            It.IsAny<Func<int, string, Task>>(),
            It.IsAny<Func<byte[], Task>>()))
            .Throws(() => new Exception("unexpected zip exception"));

        await _hub.GrxmlZipConvert(TEST_XML_STRING);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("[GrxmlZipConvert] Error in GrxmlZipConvert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GrxmlConvert_WithValidBytes_SendsProgressAndCompleted()
    {
        _gptChatMock.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(),
            It.IsAny<Func<int, string, Task>>(),
            It.IsAny<Func<string, Task>>()))
            .Returns(async (string s, Func<int, string, Task> progress, Func<string, Task> completed) =>
            {
                await progress(100, "Done");
                await completed("yaml: result");
                return "yaml: result";
            });

        await _hub.GrxmlConvert(TEST_XML_STRING);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("Progress=100, Message=Done", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logMessages, m => m.Contains($"Conversion completed. ConnectionId={HASHED_TEST_CONNECTION_ID}", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public async Task GrxmlConvert_When_EmptyFile_Then_ErrorLogged()
    {
        await _hub.GrxmlConvert([]);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains($"[GrxmlConvert] Called with null or empty bytes. ConnectionId={HASHED_TEST_CONNECTION_ID}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GrxmlConvert_WhenExceptionThrown_SendsError()
    {
        _gptChatMock.Setup(x => x.ConvertFileAsync(
            It.IsAny<string>(),
            It.IsAny<Func<int, string, Task>>(),
            It.IsAny<Func<string, Task>>()))
            .ThrowsAsync(new Exception("unexpected grxml exception"));

        await _hub.GrxmlConvert(TEST_XML_STRING);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("Error in GrxmlConvert. ConnectionId=", StringComparison.OrdinalIgnoreCase));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
                _hub.Dispose();

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
