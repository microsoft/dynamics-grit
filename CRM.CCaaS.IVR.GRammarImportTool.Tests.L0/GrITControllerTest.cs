using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0;

public class GrITControllerTest : IClassFixture<BaseTest>, IDisposable
{
    private readonly BaseTest _baseTest;

    private readonly Mock<IGptChat> _gptChatMock = new();
    private readonly Mock<IOptions<GptChatGrxmlConfiguration>> _optionsMock = new();
    private readonly GptChatGrxmlConfiguration _config = new()
    {
        AllowedUploadFileSizeRangeBytes = 1024,
        MaxAllowedConversionTimeSingleFileSec = 10,
        MaxRetries = 1,
        RetryDelaySec = 1,
        ResultStreamChannelCapacity = 10
    };

    private readonly Mock<IFormFile> _fileMockZip = new();
    private readonly Mock<IFormFile> _fileMockEmptyZip = new();
    private readonly Mock<IFormFile> _fileMockTooBigZip = new();

    private readonly Mock<IFormFile> _fileMockGrxml = new();

    private readonly HttpContext _httpContext = new DefaultHttpContext();
    private readonly MemoryStream _responseBody = new();
    private bool _disposedValue;

    public GrITControllerTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
        {
            throw new InvalidOperationException("ServiceProvider is not initialized.");
        }

        var content = new MemoryStream([0, 1, 2]);
        _fileMockZip.Setup(f => f.Length).Returns(3);
        _fileMockZip.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>(content.CopyToAsync);


        _fileMockEmptyZip.Setup(f => f.Length).Returns(0);
        _fileMockEmptyZip.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((stream, token) => Task.CompletedTask);

        _fileMockTooBigZip.Setup(f => f.Length).Returns(1025);
        _fileMockTooBigZip.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((stream, token) => Task.CompletedTask);

        var grxmlContent = "<test>grxml</test>";
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(grxmlContent));
        _fileMockGrxml.Setup(f => f.Length).Returns(grxmlContent.Length);
        _fileMockGrxml.Setup(f => f.OpenReadStream()).Returns(stream);
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

    private GrITController SetupGritController()
    {
        var controller = new GrITController();
        controller.ControllerContext = new ControllerContext { HttpContext = _httpContext };
        return controller;
    }

    [Fact]
    public async Task PostGritZipResponse_WithValidZipFile_WritesYamlToResponse()
    {
        var channel = Channel.CreateBounded<KeyValuePair<string, string>>(1);
        _gptChatMock.Setup(x => x.ConvertZipAsync(It.IsAny<Stream>(), It.IsAny<Channel<KeyValuePair<string, string>>>()))
            .Returns(async (Stream s, Channel<KeyValuePair<string, string>> ch) =>
            {
                await ch.Writer.WriteAsync(new KeyValuePair<string, string>("file.yaml", "yaml: content"));
                ch.Writer.Complete();
                return "done";
            });

        var controller = SetupGritController();

        await controller.PostGritZipResponse(_fileMockZip.Object, _gptChatMock.Object, _optionsMock.Object);

        var responseText = await GetResponseTextAsync();
        Assert.Contains("file.yaml", responseText, StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("yaml: content", responseText, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task PostGritZipResponse_WithEmptyFileForm_Throws_ArgumentOutOfRangeException()
    {
        var controller = SetupGritController();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            controller.PostGritZipResponse(_fileMockEmptyZip.Object, _gptChatMock.Object, _optionsMock.Object));
    }

    [Fact]
    public async Task PostGritZipResponse_WithTooBigFileForm_Throws_ArgumentOutOfRangeException()
    {
        var controller = SetupGritController();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            controller.PostGritZipResponse(_fileMockTooBigZip.Object, _gptChatMock.Object, _optionsMock.Object));
    }

    [Fact]
    public async Task PostGritZiplResponse_WithConversionException_WritesErrorToResponse()
    {
        _gptChatMock.Setup(x => x.ConvertZipAsync(It.IsAny<Stream>(), It.IsAny<Channel<KeyValuePair<string, string>>>()))
            .Throws<Exception>(() => new Exception("something went wrong"));

        var controller = SetupGritController();

        await controller.PostGritZipResponse(_fileMockZip.Object, _gptChatMock.Object, _optionsMock.Object);

        var responseText = await GetResponseTextAsync();
        Assert.Contains("An error occurred while processing the file.", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostGritZipResponse_WithConversionCancelledException_WritesErrorToResponse()
    {
        _gptChatMock.SetupSequence(x => x.ConvertZipAsync(It.IsAny<Stream>(), It.IsAny<Channel<KeyValuePair<string, string>>>()))
            .Throws<OperationCanceledException>(() => new OperationCanceledException("something went wrong"));

        var controller =SetupGritController();

        await controller.PostGritZipResponse(_fileMockZip.Object, _gptChatMock.Object, _optionsMock.Object);

        var responseText = await GetResponseTextAsync();
        Assert.Contains("File processing was cancelled", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostGritGrxmlResponse_WithValidFile_WritesYamlToResponse()
    {

        _gptChatMock.Setup(x => x.ConvertFileAsync(It.IsAny<string>()))
            .ReturnsAsync("yaml: content");

        var controller = SetupGritController();

        await controller.PostGritGrxmlResponse(_fileMockGrxml.Object, _gptChatMock.Object, _optionsMock.Object);

        var responseText = await GetResponseTextAsync();
        Assert.Contains("test.grxml", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("yaml: content", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostGritGrxmlResponse_WithConversionError_WritesErrorToResponse()
    {
        _gptChatMock.Setup(x => x.ConvertFileAsync(It.IsAny<string>()))
            .ReturnsAsync("Error: something went wrong");

        var controller = SetupGritController();

        await controller.PostGritGrxmlResponse(_fileMockGrxml.Object, _gptChatMock.Object, _optionsMock.Object);

        var responseText = await GetResponseTextAsync();
        Assert.Contains("Conversion failed", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Error: something went wrong", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostGritGrxmlResponse_WithEmptyFileForm_Throws_ArgumentOutOfRangeException()
    {
        var controller = SetupGritController();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            controller.PostGritGrxmlResponse(_fileMockEmptyZip.Object, _gptChatMock.Object, _optionsMock.Object));
    }

    [Fact]
    public async Task PostGritGrxmlResponse_WithTooBigFileForm_Throws_ArgumentOutOfRangeException()
    {
        var controller = SetupGritController();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            controller.PostGritGrxmlResponse(_fileMockTooBigZip.Object, _gptChatMock.Object, _optionsMock.Object));
    }

    [Fact]
    public async Task PostGritGrxmlResponse_WithConversionException_WritesErrorToResponse()
    {
        _gptChatMock.Setup(x => x.ConvertFileAsync(It.IsAny<string>()))
            .Throws<Exception>(() => new Exception("something went wrong"));

        var controller = SetupGritController();

        await controller.PostGritGrxmlResponse(_fileMockGrxml.Object, _gptChatMock.Object, _optionsMock.Object);

        var responseText = await GetResponseTextAsync();
        Assert.Contains("Unexpected error occurred", responseText, StringComparison.OrdinalIgnoreCase);

    }

    [Fact]
    public async Task PostGritGrxmlResponse_WithConversionCancelledException_WritesErrorToResponse()
    {
        _gptChatMock.SetupSequence(x => x.ConvertFileAsync(It.IsAny<string>()))
            .Throws<OperationCanceledException>(() => new OperationCanceledException("something went wrong"));

        var controller = SetupGritController();

        await controller.PostGritGrxmlResponse(_fileMockGrxml.Object, _gptChatMock.Object, _optionsMock.Object);

        var responseText = await GetResponseTextAsync();
        Assert.Contains("File processing was cancelled", responseText, StringComparison.OrdinalIgnoreCase);
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _responseBody.Dispose();
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
