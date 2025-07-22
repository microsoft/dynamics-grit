using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using YamlDotNet.Core;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0;

public class GptChatGrxmlToMcsConverterTest : IClassFixture<BaseTest>, IDisposable
{
    private const string TestValidXml = @"<grammar version=""1.0""
xml:lang=""en-US""
mode=""voice""
xmlns=""http://www.w3.org/2001/06/grammar""
xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
xsi:schemaLocation=""http://www.w3.org/2001/06/grammar
http://www.w3.org/TR/speech-grammar/grammar.xsd""
root=""MAIN""
tag-format=""semantics/1.0"">

            <!-- This is a comment -->

<meta name=""swirec_simple_result_key"" content=""SWI_literal""/>

<rule id=""test"" xsi:nil=""true"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""/>

<rule id=""MAIN"" scope=""public"">
<ruleref special=""GARBAGE""/> title     </rule>

<rule id=""MAIN2"" scope=""public"">
<ruleref special=""GARBAGE""/>      </rule>
</grammar>";

    private const string TestValidXml2 = @"<grammar version=""1.0""
xml:lang=""en-US""
mode=""voice""
xmlns=""http://www.w3.org/2001/06/grammar""
xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
xsi:schemaLocation=""http://www.w3.org/2001/06/grammar
http://www.w3.org/TR/speech-grammar/grammar.xsd""
root=""MAIN""
tag-format=""semantics/1.0"">

            <!-- This is a comment -->
<meta name=""swirec_simple_result_key"" content=""SWI_literal""/>

<!-- This is a comment -->
<rule id=""MAIN"" scope=""public"">
<ruleref special=""GARBAGE""/> today     </rule>
</grammar>";

    private static readonly string HashTestValidXml = GetStringSha256Hash(TestValidXml);
    private static readonly string HashTestValidXml2 = GetStringSha256Hash(TestValidXml2);

    private bool _disposedValue;
    private readonly GptChatGrxmlToMcsConverter _converter;
    private readonly BaseTest _baseTest;

    public GptChatGrxmlToMcsConverterTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
        {
            throw new InvalidOperationException("ServiceProvider is not initialized.");
        }

        _converter = new GptChatGrxmlToMcsConverter(
            _baseTest.ServiceProvider.GetRequiredService<IOptions<GptChatGrxmlConfiguration>>(),
            _baseTest.ServiceProvider.GetRequiredService<IAzureOpenAIClientFactory>());
    }

    private static MemoryStream CreateZipStream(string fileName1, string fileName2)
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            var entry1 = archive.CreateEntry(fileName1);
            using (var writer = new StreamWriter(entry1.Open(), Encoding.UTF8))
            {
                writer.Write(TestValidXml);
            }
            var entry2 = archive.CreateEntry(fileName2);
            using (var writer = new StreamWriter(entry2.Open(), Encoding.UTF8))
            {
                writer.Write(TestValidXml2);
            }
        }
        zipStream.Position = 0; // Reset stream position for reading
        return zipStream;
    }
    private static string GetStringSha256Hash(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        byte[] textData = Encoding.UTF8.GetBytes(text);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(textData);
        return BitConverter.ToString(hash).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void When_GetIAzureOpenAIClientFactory_Then_ReturnsNotNull()
    {
        var factory = _converter.AzureOpenAIClientFactory;
        Assert.NotNull(factory);
        Assert.IsType<IAzureOpenAIClientFactory>(factory, exactMatch: false);
    }

    [Fact]
    public void When_ValidZipWithUniqueFiles_Then_ReturnsCorrectDictionary()
    {
        using var zipStream = CreateZipStream("file1.grxml", "file2.grxml");
        var result = _converter.LoadZipToDictionary(zipStream);

        Assert.Equal(2, result.Count);
        Assert.Equal(HashTestValidXml, GetStringSha256Hash(result["file1.grxml"]));
        Assert.Equal(HashTestValidXml2, GetStringSha256Hash(result["file2.grxml"]));
    }

    [Fact]
    public void When_DuplicateFileNames_Then_AppendsSuffix()
    {
        using var zipStream = CreateZipStream("duplicate.grxml", "duplicate.grxml");
        var result = _converter.LoadZipToDictionary(zipStream);

        Assert.Equal(2, result.Count);
        Assert.Contains("duplicate.grxml", result.Keys);
        Assert.Contains("duplicate-1.grxml", result.Keys);
        Assert.Equal(HashTestValidXml, GetStringSha256Hash(result["duplicate.grxml"]));
        Assert.Equal(HashTestValidXml2, GetStringSha256Hash(result["duplicate-1.grxml"]));
    }

    [Fact]
    public void When_LoadZipToDictionary_EmptyZip_Then_ThrowsException()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true)) { }
        ms.Position = 0;

        Assert.Throws<InvalidDataException>(() => _converter.LoadZipToDictionary(ms));
    }

    [Fact]
    public void When_Create_Then_NoErrors()
    {
        if (_baseTest.ServiceProvider == null)
        {
            throw new InvalidOperationException("ServiceProvider is not initialized.");
        }

        var converter = _baseTest.ServiceProvider.GetKeyedService<IGptChat>(GptChatGrxmlToMcsConverter.SERVICE_KEY);
        Assert.NotNull(converter);
    }

    [Fact]
    public void When_ValidateGoodYaml_Then_NoError()
    {
        _converter?.ValidateYamlContent("---\ntest: content");
        Assert.Throws<SyntaxErrorException>(() => _converter?.ValidateYamlContent(
            @"---invalid yaml content:
indeed invalid"));
    }

    [Fact]
    public void When_ValidateGoodYaml_Then_SyntaxErrorException()
    {
        _converter?.ValidateYamlContent("---\ntest: content");
        Assert.Throws<SyntaxErrorException>(() => _converter?.ValidateYamlContent(
            @"---invalid yaml content:
indeed invalid"));
    }

    [Fact]
    public void When_TryReadXmlContent_Then_RemovesCommentsAndWhitespace()
    {

        bool result = _converter.TryReadXmlContent("good.xml", TestValidXml, out var stripped);

        Assert.True(result);
        Assert.Contains("</grammar>", stripped, StringComparison.CurrentCultureIgnoreCase);
        Assert.DoesNotContain("<!--", stripped, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"</root><child></root>")]
    [InlineData(@"")]
    public void When_TryReadXmlContent_InvalidXml_ReturnsFalse(string xmlContent)
    {
        bool result = _converter.TryReadXmlContent("invalid.xml", xmlContent, out var stripped);

        Assert.False(result);
        Assert.Equal("Error: Failed to parse as XML.", stripped);
    }

    [Fact]
    public void When_CreateResultZipStream_ValidDictionary_Then_CreatesZipWithCorrectFilesAndContent()
    {
        var results = new ConcurrentDictionary<string, string>();
        results["file1.grxml"] = TestValidXml;
        results["file2.grxml"] = TestValidXml2;
        string extension = ".yaml";

        using var zipStream = _converter.CreateResultZipStream(results, extension);
        Assert.NotNull(zipStream);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, e => e.Name == "file1.yaml");
        Assert.Contains(archive.Entries, e => e.Name == "file2.yaml");

        var entry1 = archive.GetEntry("file1.yaml");
        Assert.NotNull(entry1);
        using var reader1 = new StreamReader(entry1.Open());
        Assert.Equal(HashTestValidXml, GetStringSha256Hash(reader1.ReadToEnd()));

        var entry2 = archive.GetEntry("file2.yaml");
        Assert.NotNull(entry2);
        using var reader2 = new StreamReader(entry2.Open());
        Assert.Equal(HashTestValidXml2, GetStringSha256Hash(reader2.ReadToEnd()));
    }

    [Fact]
    public void When_CreateResultZipStream_EmptyDictionary_Then_ThrowException()
    {
        var results = new ConcurrentDictionary<string, string>();
        string extension = ".yaml";

        Assert.Throws<InvalidDataException>(() => _converter.CreateResultZipStream(results, extension));
    }

    [Fact]
    public async Task When_ProcessSingleFileAsync_WithValidXml_Then_ReturnsExpectedYaml()
    {
        var result = await _converter.ProcessSingleFileAsync("good.grxml", TestValidXml);

        Assert.NotNull(result);
        Assert.Contains("hello: result", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Error:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_ProcessSingleFileAsync_WithInvalidXml_Then_ReturnsErrorMessage()
    {
        var invalidXml = "</root><child></root>";

        var result = await _converter.ProcessSingleFileAsync("clientException.grxml", invalidXml);

        Assert.NotNull(result);
        Assert.Contains("Error:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Failed to process clientException.grxml", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_ProcessSingleFileAsync_ReturnsBadYaml_Then_ExceptionThrown()
    {
        var emptyContent = "";

        var result = await _converter.ProcessSingleFileAsync("badYaml.grxml", emptyContent);

        Assert.NotNull(result);
        Assert.Contains("Error:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Failed to process badYaml.grxml", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_ConvertZipAsyncR_ValidZip_Then_ReturnsZipStreamWithYamlFiles()
    {
        using var zipStream = CreateZipStream("file1.grxml", "file2.grxml");
        var progressUpdates = new List<(int, string)>();
        byte[]? completedBytes = null;

        Task ProgressCallback(int progress, string message)
        {
            progressUpdates.Add((progress, message));
            return Task.CompletedTask;
        }

        Task CompletedCallback(byte[] bytes)
        {
            completedBytes = bytes;
            return Task.CompletedTask;
        }

        var resultStream = await _converter.ConvertZipAsync(zipStream, ProgressCallback, CompletedCallback);

        Assert.NotNull(resultStream);
        Assert.NotNull(completedBytes);
        using var archive = new ZipArchive(resultStream, ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        Assert.Contains(archive.Entries, e => e.Name == "file1.yaml");
        Assert.Contains(archive.Entries, e => e.Name == "file2.yaml");
        Assert.True(progressUpdates.Count > 0);
    }

    [Fact]
    public async Task When_ConvertZipAsyncR_EmptyZip_Then_ThrowsInvalidDataException()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true)) { }
        ms.Position = 0;

        Task ProgressCallback(int progress, string message) => Task.CompletedTask;
        Task CompletedCallback(byte[] bytes) => Task.CompletedTask;

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await _converter.ConvertZipAsync(ms, ProgressCallback, CompletedCallback));
    }

    [Fact]
    public async Task When_ConvertZipAsyncR_InvalidXml_Then_ErrorIsReturnedInZip()
    {
        // Arrange
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("bad.grxml");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            {
                writer.Write("</root><child></root>");
            }
        }
        zipStream.Position = 0;

        Task ProgressCallback(int progress, string message) => Task.CompletedTask;
        byte[]? completedBytes = null;
        Task CompletedCallback(byte[] bytes)
        {
            completedBytes = bytes;
            return Task.CompletedTask;
        }

        var resultStream = await _converter.ConvertZipAsync(zipStream, ProgressCallback, CompletedCallback);

        Assert.NotNull(resultStream);
        using var archiveResult = new ZipArchive(resultStream, ZipArchiveMode.Read);
        var entryResult = archiveResult.GetEntry("bad.yaml");
        Assert.NotNull(entryResult);
        using var reader = new StreamReader(entryResult.Open());
        var content = reader.ReadToEnd();
        Assert.Contains("Can't parse this XML", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_ConvertZipAsync_UnknownException_Then_ErrorIsReturnedInZip()
    {
        // Arrange
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("unKnownException.grxml");
            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            {
                writer.Write("<root>test</root>");
            }
        }
        zipStream.Position = 0;

        Task ProgressCallback(int progress, string message) => Task.CompletedTask;
        byte[]? completedBytes = null;
        Task CompletedCallback(byte[] bytes)
        {
            completedBytes = bytes;
            return Task.CompletedTask;
        }

        var resultStream = await _converter.ConvertZipAsync(zipStream, ProgressCallback, CompletedCallback);

        Assert.NotNull(resultStream);
        using var archiveResult = new ZipArchive(resultStream, ZipArchiveMode.Read);
        var entryResult = archiveResult.GetEntry("unKnownException.yaml");
        Assert.NotNull(entryResult);
        using var reader = new StreamReader(entryResult.Open());
        var content = reader.ReadToEnd();
        Assert.Contains("Error: Unexpected error occured", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_ConvertValidFileAsync_Then_GoodResult()
    {
        var result = await _converter.ConvertFileAsync(TestValidXml);

        Assert.NotNull(result);
        Assert.Contains("hello: result", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_ConvertInvalidFileAsync_Then_Error()
    {
        var result = await _converter.ConvertFileAsync("</root><child></root>");

        Assert.NotNull(result);
        Assert.Contains("Error: Failed to parse as XML", result, StringComparison.OrdinalIgnoreCase);
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
