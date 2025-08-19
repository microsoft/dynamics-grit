using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Controllers;

public class GrITControllerAsyncTest : IClassFixture<BaseTest>, IDisposable
{
    private readonly BaseTest _baseTest;
    private readonly Mock<IBackgroundTaskQueue> _queueMock = new();
    private readonly Mock<JobTracker> _trackerMock = new();
    private readonly Mock<IGptChat> _gptChatMock = new();
    private readonly Mock<IOptions<GptChatGrxmlConfiguration>> _optionsMock = new();
    private readonly GptChatGrxmlConfiguration _config = new()
    {
        AllowedUploadFileSizeRangeBytes = 1024,
        MaxAllowedConversionTimeSingleFileSec = 10,
        ResultStreamChannelCapacity = 10
    };

    private readonly Mock<IFormFile> _fileMockZip = new();
    private readonly Mock<IFormFile> _fileMockGrxml = new();
    private readonly HttpContext _httpContext = new DefaultHttpContext();
    private readonly MemoryStream _responseBody = new();
    private bool _disposedValue;

    public GrITControllerAsyncTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        var zipContent = new MemoryStream([0, 1, 2]);
        _fileMockZip.Setup(f => f.FileName).Returns("test.zip");
        _fileMockZip.Setup(f => f.Length).Returns(3);
        _fileMockZip.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>(zipContent.CopyToAsync);

        var grxmlContent = "<test>grxml</test>";
        var grxmlStream = new MemoryStream(Encoding.UTF8.GetBytes(grxmlContent));
        _fileMockGrxml.Setup(f => f.Length).Returns(grxmlContent.Length);
        _fileMockGrxml.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>(grxmlStream.CopyToAsync);
        _fileMockGrxml.Setup(f => f.FileName).Returns("test.grxml");

        _optionsMock.Setup(o => o.Value).Returns(_config);
        _baseTest.LogProvider.Logger.Clear();

        _httpContext.Response.Body = _responseBody;
    }

    private async Task<string> GetResponseTextAsync()
    {
        _responseBody.Position = 0;
        using var reader = new StreamReader(_responseBody);
        return await reader.ReadToEndAsync();
    }

    private GrITControllerAsync SetupController()
    {
        var controller = new GrITControllerAsync(_queueMock.Object, _trackerMock.Object, _baseTest.ServiceProvider!);
        controller.ControllerContext = new ControllerContext { HttpContext = _httpContext };
        return controller;
    }

    [Fact]
    public async Task When_AddZipTask_WithValidZipFile_Then_EnqueuesJobAndReturnsAccepted()
    {
        _queueMock.Setup(q => q.EnqueueAsync(It.IsAny<JobTask>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _trackerMock.Setup(t => t.SetStatus(It.IsAny<string>(), JobStatus.Pending));

        var controller = SetupController();

        var result = await controller.AddZipTask(_fileMockZip.Object, _gptChatMock.Object, _optionsMock.Object, new CancellationToken());
        var objectResult = result as ObjectResult;

        Assert.NotNull(objectResult);
        Assert.NotNull(objectResult.Value);
        Assert.Equal(202, objectResult.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(objectResult.Value.ToString()));
    }

    [Fact]
    public async Task When_AddZipTask_EnqueueFails_Then_ReturnsServiceUnavailable()
    {
        _queueMock.Setup(q => q.EnqueueAsync(It.IsAny<JobTask>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var controller = SetupController();

        var result = await controller.AddZipTask(_fileMockZip.Object, _gptChatMock.Object, _optionsMock.Object, new CancellationToken());
        var objectResult = result as ObjectResult;

        Assert.NotNull(objectResult);
        Assert.NotNull(objectResult.Value);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Contains("Failed to enqueue job", objectResult.Value.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_AddGrxmlTask_WithValidFile_Then_EnqueuesJobAndReturnsAccepted()
    {
        _queueMock.Setup(q => q.EnqueueAsync(It.IsAny<JobTask>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _trackerMock.Setup(t => t.SetStatus(It.IsAny<string>(), JobStatus.Pending));

        var controller = SetupController();

        var result = await controller.AddGrxmlTask(_fileMockGrxml.Object, _gptChatMock.Object, _optionsMock.Object, new CancellationToken());
        var objectResult = result as ObjectResult;

        Assert.NotNull(objectResult);
        Assert.NotNull(objectResult.Value);
        Assert.Equal(202, objectResult.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(objectResult.Value.ToString()));
    }

    [Fact]
    public async Task When_AddGrxmlTask_EnqueueFails_Then_ReturnsServiceUnavailable()
    {
        _queueMock.Setup(q => q.EnqueueAsync(It.IsAny<JobTask>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var controller = SetupController();

        var result = await controller.AddGrxmlTask(_fileMockGrxml.Object, _gptChatMock.Object, _optionsMock.Object, new CancellationToken());
        var objectResult = result as ObjectResult;

        Assert.NotNull(objectResult);
        Assert.NotNull(objectResult.Value);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Contains("Failed to enqueue job", objectResult.Value.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void When_GetStatus_JobExists_Then_ReturnsOkWithStatus()
    {
        _trackerMock.Setup(t => t.GetStatus("job123")).Returns(JobStatus.Pending);

        var controller = SetupController();

        var result = controller.GetStatus("job123");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("Pending", okResult.Value?.ToString(), StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void When_GetStatus_JobDoesNotExist_Then_ReturnsNotFound()
    {
        _trackerMock.Setup(t => t.GetStatus("job123")).Returns((JobStatus?)null);

        var controller = SetupController();

        var result = controller.GetStatus("job123");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task When_GetResultsAsync_JobCompleted_Then_ReturnsOkWithResults()
    {
        _trackerMock.Setup(t => t.GetStatus("job123")).Returns(JobStatus.Completed);

        var controller = SetupController();

        var result = await controller.GetResultsAsync("job123", "pretty");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("file.yaml", okResult.Value?.ToString(), StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("yaml: content", okResult.Value?.ToString(), StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public async Task When_GetResultsAsync_JobCompleted_NoResults_Then_ReturnsOkWithEmptyResults()
    {
        _trackerMock.Setup(t => t.GetStatus("jobResultIsNull")).Returns(JobStatus.Completed);

        var controller = SetupController();

        var result = await controller.GetResultsAsync("jobResultIsNull", "json");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains(@"{""JobId"":""jobResultIsNull"",""Results"":{""ResultData"":{},""CreatedAt"":",
            JsonSerializer.Serialize(okResult?.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_GetResultsAsync_JobNotCompleted_Then_ReturnsOkWithStatus()
    {
        _trackerMock.Setup(t => t.GetStatus("job123")).Returns(JobStatus.Pending);

        var controller = SetupController();

        var result = await controller.GetResultsAsync("job123", "json");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("Pending", okResult.Value?.ToString(), StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public async Task When_GetResultsAsync_JobDoesNotExist_Then_ReturnsNotFound()
    {
        _trackerMock.Setup(t => t.GetStatus("job123")).Returns((JobStatus?)null);

        var controller = SetupController();

        var result = await controller.GetResultsAsync("job123", "json");

        Assert.IsType<NotFoundResult>(result);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
                _responseBody.Dispose();
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
