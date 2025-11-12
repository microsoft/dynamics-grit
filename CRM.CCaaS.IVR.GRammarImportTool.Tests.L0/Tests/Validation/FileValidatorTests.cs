using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Grxml;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Validation;

[Collection("BaseTestCollection")]
public class FileValidatorTests : IDisposable
{
    private readonly BaseTest _baseTest;
    private readonly FileValidator _validator;
    private bool _disposedValue;

    public FileValidatorTests(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        _validator = new FileValidator(_baseTest.ServiceProvider.GetRequiredService<IOptions<GptChatGrxmlConfiguration>>());

        _baseTest.LogProvider.Logger.Clear();
    }

    [Fact]
    public async Task ValidateUploadedFileAsync_ValidXmlFile_ReturnsSuccess()
    {
        _baseTest.GptChatGrxmlTestConfiguration.AllowedUploadFileSizeRangeBytes = 200;
        var content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><grammar></grammar>";
        var file = CreateMockFormFile(content, "test.grxml", "application/xml");

        var result = await _validator.ValidateUploadedFileAsync(file, true);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        _baseTest.GptChatGrxmlTestConfiguration.AllowedUploadFileSizeRangeBytes = 50;
    }

    [Fact]
    public async Task ValidateUploadedFileAsync_InvalidFileExtension_ReturnsFailure()
    {
        var content = "Some text";
        var file = CreateMockFormFile(content, "test.txt", "text/plain");

        var result = await _validator.ValidateUploadedFileAsync(file);

        Assert.False(result.IsValid);
        Assert.Contains("Unsupported file type", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateUploadedFileAsync_MaliciousXmlContent_ReturnsFailure()
    {
        _baseTest.GptChatGrxmlTestConfiguration.AllowedUploadFileSizeRangeBytes = 200;
        var content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE test [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><grammar>&xxe;</grammar>";
        var file = CreateMockFormFile(content, "malicious.xml", "application/xml");

        var result = await _validator.ValidateUploadedFileAsync(file, true);

        Assert.False(result.IsValid);
        Assert.Contains("potentially unsafe content", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        _baseTest.GptChatGrxmlTestConfiguration.AllowedUploadFileSizeRangeBytes = 50;
    }

    [Fact]
    public async Task ValidateUploadedFileAsync_FileExceedingSizeLimit_ReturnsFailure()
    {
        var content = "This is a content for the test file with more than 30 bytes";
        var file = CreateMockFormFile(content, "test.grxml", "application/xml");

        var result = await _validator.ValidateUploadedFileAsync(file);

        Assert.False(result.IsValid);
        Assert.Contains("exceeds maximum allowed size", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateUploadedFileAsync_EmptyFile_ReturnsFailure()
    {
        var file = CreateMockFormFile("", "empty.grxml", "application/xml");

        var result = await _validator.ValidateUploadedFileAsync(file);

        Assert.False(result.IsValid);
        Assert.Contains("File is empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateUploadedFileAsync_JavaScriptInjection_ReturnsFailure()
    {
        var content = "<?xml version=\"1.0\"?><script>alert('XSS')</script>";
        var file = CreateMockFormFile(content, "malicious.grxml", "application/xml");

        var result = await _validator.ValidateUploadedFileAsync(file);

        Assert.False(result.IsValid);
        Assert.Contains("potentially unsafe content", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateUploadedFileAsync_BadMime_ReturnsFailure()
    {
        var content = "<grammar>hello</grammar>";
        var file = CreateMockFormFile(content, "badMime.xml", "application/bad-zip");

        var result = await _validator.ValidateUploadedFileAsync(file, true);

        Assert.False(result.IsValid);
        Assert.Contains("Unsupported file type based on content", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateUploadedFileAsync_Zip_Success()
    {
        var content = "<grammar>hello</grammar>";
        var file = CreateMockFormFile(content, "ZIP.zip", "application/zip");

        var result = await _validator.ValidateUploadedFileAsync(file, true);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task When_ValidateZipContentsAsync_with_correctZIP_Then_ReturnSuccess()
    {
        using var zipStream = GptChatGrxmlToMcsConverterTest.CreateZipStream("file1.grxml", "file2.grxml");
        var result = await _validator.ValidateZipContentsAsync(zipStream);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task When_ValidateZipContentsAsync_with_badZIP_Then_ReturnFailure()
    {
        using var zipStream = GptChatGrxmlToMcsConverterTest.CreateZipStream("file1.grxml", "<script>im bad<script/>");
        var result = await _validator.ValidateZipContentsAsync(zipStream);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task When_ValidateZipContentsAsync_with_emptyZIP_Then_ReturnFailure()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true)) { }
        ms.Position = 0;

        var result = await _validator.ValidateZipContentsAsync(ms);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task When_ValidateZipContentsAsync_with_null_Then_ReturnFailure()
    {
        using var ms = new MemoryStream();
        ms.Close();

        var result = await _validator.ValidateZipContentsAsync(ms);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task When_ValidateZipContentsAsync_with_BadZip_Then_ReturnFailure()
    {
        using var ms = new MemoryStream([1, 2, 3, 4]);

        var result = await _validator.ValidateZipContentsAsync(ms);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task When_ValidateZipContentsAsync_with_MaxEntryCount_Then_ReturnFailure()
    {
        using var zipStream = GptChatGrxmlToMcsConverterTest.CreateZipStream("file1.grxml", "file2.grxml");

        var save = _baseTest.GptChatGrxmlTestConfiguration.MaxEntryCount;
        _baseTest.GptChatGrxmlTestConfiguration.MaxEntryCount = 1;

        var result = await _validator.ValidateZipContentsAsync(zipStream);
        Assert.False(result.IsValid);
        _baseTest.GptChatGrxmlTestConfiguration.MaxEntryCount = save;
    }

    [Fact]
    public async Task When_ValidateZipContentsAsync_with_MaxEntrySize_Then_ReturnFailure()
    {
        using var zipStream = GptChatGrxmlToMcsConverterTest.CreateZipStream("file1.grxml", "<grammar>too big grammar to be handled</grammar>");

        var save = _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize;
        _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize = 5;

        var result = await _validator.ValidateZipContentsAsync(zipStream);
        Assert.False(result.IsValid);
        _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize = save;
    }

    [Fact]
    public async Task When_ValidateZipContentsAsync_with_MaxTotalUncompressedEntrySize_Then_ReturnFailure()
    {
        using var zipStream = GptChatGrxmlToMcsConverterTest.CreateZipStream("file1.grxml", "<grammar>too big grammar to be handled</grammar>");

        var save = _baseTest.GptChatGrxmlTestConfiguration.MaxTotalUncompressedSize;
        _baseTest.GptChatGrxmlTestConfiguration.MaxTotalUncompressedSize = 10;

        var result = await _validator.ValidateZipContentsAsync(zipStream);
        _baseTest.GptChatGrxmlTestConfiguration.MaxTotalUncompressedSize = save;

        Assert.False(result.IsValid);
    }

    private static IFormFile CreateMockFormFile(string content, string fileName, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        var file = new Mock<IFormFile>();

        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.ContentType).Returns(contentType);
        file.Setup(f => f.Length).Returns(bytes.Length);
        file.Setup(f => f.OpenReadStream()).Returns(stream);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, CancellationToken>((s, token) =>
            {
                stream.Position = 0;
                stream.CopyTo(s);
            })
            .Returns(Task.CompletedTask);

        return file.Object;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~FileValidatorTests()
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
