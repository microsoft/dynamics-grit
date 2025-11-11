using System.Collections.Concurrent;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using Castle.Components.DictionaryAdapter.Xml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using YamlDotNet.Core;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Grxml;

[Collection("BaseTestCollection")]
public class GptChatGrxmlToMcsConverterTest : IDisposable
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
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        _converter = new GptChatGrxmlToMcsConverter(
            _baseTest.ServiceProvider.GetRequiredService<IOptions<GptChatGrxmlConfiguration>>(),
            _baseTest.ServiceProvider.GetRequiredService<IChatService>(),
            _baseTest.ServiceProvider.GetRequiredService<AiContentValidator>(),
            _baseTest.ServiceProvider.GetRequiredService<TokenValidator>());

        _baseTest.LogProvider.Logger.Clear();
    }

    public static MemoryStream CreateZipStream(string fileName1, string fileName2)
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
            var entry3 = archive.CreateEntry($"dir1/placeholder.grxml");
            using (var writer = new StreamWriter(entry3.Open(), Encoding.UTF8))
            {
                writer.Write(TestValidXml2);
            }
            archive.CreateEntry($"dir2/");
        }
        zipStream.Position = 0; // Reset stream position for reading
        return zipStream;
    }

    public static MemoryStream CreateZipStream(List<string> fileNames, string content)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            foreach (var fileName in fileNames)
            {
                var entry = archive.CreateEntry(fileName);
                using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    writer.Write(content);
                }
            }
        }
        zipStream.Position = 0; // Reset stream position for reading
        return zipStream;
    }

    private static string GetStringSha256Hash(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var textData = Encoding.UTF8.GetBytes(text);
        var hash = System.Security.Cryptography.SHA256.HashData(textData);
        return BitConverter.ToString(hash).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void When_GetOpenAIChatService_Then_ReturnsNotNull()
    {
        var factory = _converter.OpenAIChatService;
        Assert.NotNull(factory);
        Assert.IsType<IChatService>(factory, exactMatch: false);
    }

    [Fact]
    public async Task When_ValidZipWithUniqueFiles_Then_ReturnsCorrectDictionary()
    {
        using var zipStream = CreateZipStream("file1.grxml", "file2.grxml");
        var result = await _converter.LoadZipToDictionaryAsync(zipStream);

        Assert.Equal(3, result.Count);
        Assert.Equal(HashTestValidXml, GetStringSha256Hash(result["file1.grxml"]));
        Assert.Equal(HashTestValidXml2, GetStringSha256Hash(result["file2.grxml"]));
    }

    [Fact]
    public async Task When_DuplicateFileNames_Then_AppendsSuffix()
    {
        using var zipStream = CreateZipStream("duplicate.grxml", "duplicate.grxml");
        var result = await _converter.LoadZipToDictionaryAsync(zipStream);

        Assert.Equal(3, result.Count);
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

        Assert.Throws<InvalidDataException>(() => _converter.LoadZipToDictionaryAsync(ms).GetAwaiter().GetResult());
    }

    [Fact]
    public void When_Create_Then_NoErrors()
    {
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

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

        var result = _converter.TryReadXmlContent("good.xml", TestValidXml, out var stripped);

        Assert.True(result);
        Assert.Contains("</grammar>", stripped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<!--", stripped, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"</root><child></root>")]
    [InlineData(@"")]
    public void When_TryReadXmlContent_InvalidXml_ReturnsFalse(string xmlContent)
    {
        var result = _converter.TryReadXmlContent("invalid.xml", xmlContent, out var stripped);

        Assert.False(result);
        Assert.Equal("Error: Failed to parse as XML (XmlException).", stripped);
    }

    [Fact]
    public async Task When_CreateResultZipStream_ValidDictionary_Then_CreatesZipWithCorrectFilesAndContent()
    {
        var results = new ConcurrentDictionary<string, string>();
        results["file1.grxml"] = TestValidXml;
        results["file2.grxml"] = TestValidXml2;
        var extension = ".yaml";

        // Use the async method synchronously for the test
        using var zipStream = await _converter.CreateResultZipStreamAsync(results, extension);
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
        var extension = ".yaml";

        Assert.Throws<InvalidDataException>(() => _converter.CreateResultZipStreamAsync(results, extension).GetAwaiter().GetResult());
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
    public async Task When_ProcessSingleFileAsync_ValidationFails_Then_ReturnError()
    {
        var result = await _converter.ProcessSingleFileAsync("badGrxml.grxml", "<grammar>hello</grammar>");

        Assert.NotNull(result);
        Assert.Contains("Error:", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Error validating AI prompt content", result, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(3, archive.Entries.Count);
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
        var zipStream = CreateZipStream(new List<string> { "bad.grxml" }, "</root><child></root>");

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
    public async Task When_ConvertZipAsyncR_UnknownException_Then_ErrorIsReturnedInZip()
    {
        var zipStream = CreateZipStream(new List<string> { "unKnownException.grxml" }, "<root>test</root>");

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
        Assert.Contains("Error: Unknown exception occurred during processing", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_ConvertZipAsyncR_SingleFileTimeout_Then_ErrorIsLogged()
    {
        var zipStream = CreateZipStream(new List<string> { "timeout5000.grxml" }, "<root>test</root>");

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
        var entryResult = archiveResult.GetEntry("timeout5000.yaml");
        Assert.NotNull(entryResult);
        using var reader = new StreamReader(entryResult.Open());
        var content = reader.ReadToEnd();
        Assert.Contains("Error: Processing timeout for timeout5000.grxml", content, StringComparison.OrdinalIgnoreCase);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("[ProcessSingleFileAsync] Cancelled", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logMessages, m => m.Contains("[ProcessSingleFileAsync] Cancelled | Reason=Timeout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertZipAsyncR_TotalTimeout_Then_ErrorIsLogged()
    {
        var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            for (var i = 0; i < 10; i++)
            {
                var entry = archive.CreateEntry($"timeout4000_{i}.grxml");
                using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    writer.Write("<root>timeout</root>");
                }
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
        var entryResult = archiveResult.GetEntry("error.yaml");
        Assert.NotNull(entryResult);
        using var reader = new StreamReader(entryResult.Open());
        var content = reader.ReadToEnd();
        Assert.Contains("Error: Zip file processing cancelled", content, StringComparison.OrdinalIgnoreCase);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("Cancelled | Reason=TotalTimeout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertFileAsyncR_ValidGrxml_Then_ReturnsYaml()
    {
        var progressUpdates = new List<(int, string)>();
        var resultYaml = string.Empty;

        Task ProgressCallback(int progress, string message)
        {
            progressUpdates.Add((progress, message));
            return Task.CompletedTask;
        }

        Task<string> CompletedCallback(string result)
        {
            return Task.FromResult(result);
        }

        resultYaml = await _converter.ConvertFileAsync(TestValidXml, ProgressCallback, CompletedCallback);

        Assert.False(string.IsNullOrWhiteSpace(resultYaml));
        Assert.True(progressUpdates.Count == 1);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.DoesNotContain(logMessages, m => m.Contains("Error", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logMessages, m => m.Contains("Warning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertFileAsyncR_EmptyFile_Then_ThrowsInvalidDataException()
    {
        var progressUpdates = new List<(int, string)>();

        Task ProgressCallback(int progress, string message)
        {
            progressUpdates.Add((progress, message));
            return Task.CompletedTask;
        }

        Task<string> CompletedCallback(string result)
        {
            return Task.FromResult(result);
        }

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _converter.ConvertFileAsync("", ProgressCallback, CompletedCallback));
    }

    [Fact]
    public async Task When_ConvertFileAsyncR_BadXml_Then_ErrorIsLogged()
    {
        var progressUpdates = new List<(int, string)>();

        Task ProgressCallback(int progress, string message)
        {
            progressUpdates.Add((progress, message));
            return Task.CompletedTask;
        }

        Task<string> CompletedCallback(string result)
        {
            return Task.FromResult(result);
        }

        await _converter.ConvertFileAsync(@"</root><child></root>", ProgressCallback, CompletedCallback);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("[ConvertFileAsync] XmlParseError", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertZipAsync_ValidZip_Then_OutputChannelHasYamlFilesNoErrors()
    {
        using var zipStream = CreateZipStream("file1.grxml", "file2.grxml");

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);
        var result = await _converter.ConvertZipAsync(zipStream, resultsChannel);

        Assert.NotNull(result);
        Assert.Equal("Conversion complete", result);
        Assert.Equal(3, resultsChannel.Reader.Count);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("[ConvertZipAsync-Channel] FileProcessed | HashedFileName=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logMessages, m => m.Contains("[ConvertZipAsync-Channel] FileProcessed | HashedFileName=", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logMessages, m => m.Contains("[ConvertZipAsync-Channel] Error", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logMessages, m => m.Contains("[ConvertZipAsync-Channel] Warning", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logMessages, m => m.Contains("[ConvertZipAsync-Channel] AllFilesProcessed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertZipAsync_EmptyZip_Then_ThrowsInvalidDataException()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true)) { }
        ms.Position = 0;

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await _converter.ConvertZipAsync(ms, resultsChannel));
    }

    [Fact]
    public async Task When_ConvertZipAsync_InvalidXml_Then_ErrorIsLogged()
    {
        var zipStream = CreateZipStream(new List<string> { "bad.grxml" }, "</root><child></root>");

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);
        var result = await _converter.ConvertZipAsync(zipStream, resultsChannel);

        Assert.NotNull(result);
        Assert.Equal("Conversion complete", result);
        Assert.Equal(1, resultsChannel.Reader.Count);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("[ConvertZipAsync-Channel] XmlParseError | HashedFileName=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertZipAsync_UnknownException_Then_ErrorIsLogged()
    {
        var zipStream = CreateZipStream(new List<string> { "unKnownException.grxml" }, "<root>test</root>");

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);
        var result = await _converter.ConvertZipAsync(zipStream, resultsChannel);

        Assert.NotNull(result);
        Assert.Equal("Conversion complete", result);
        Assert.Equal(1, resultsChannel.Reader.Count);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("[ConvertZipAsync-Channel] UnexpectedError | HashedFileName=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertZipAsync_SingleFileTimeout_Then_ErrorIsLogged()
    {
        var zipStream = CreateZipStream(new List<string> { "timeout5000.grxml" }, "<root>test</root>");

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);
        var result = await _converter.ConvertZipAsync(zipStream, resultsChannel);

        Assert.NotNull(result);
        Assert.Equal("Conversion complete", result);
        Assert.Equal(1, resultsChannel.Reader.Count);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("[ProcessSingleFileAsync] Cancelled | Reason=Timeout", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class NonSeekableReadStream(byte[] data) : Stream
    {
        private readonly byte[] _data = data ?? throw new ArgumentNullException(nameof(data));
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(buffer));

            var remaining = _data.Length - _position;
            if (remaining <= 0) return 0;

            var toRead = Math.Min(count, remaining);
            Buffer.BlockCopy(_data, _position, buffer, offset, toRead);
            _position += toRead;
            return toRead;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task When_ConvertZipAsync_NonSeekableZip_Then_WarningIsLogged()
    {
        var seekableZip = CreateZipStream(new List<string> { "timeout5000.grxml" }, "<root>test</root>");
        var zipBytes = seekableZip.ToArray();
        using Stream zipStream = new NonSeekableReadStream(zipBytes);

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);
        var result = await _converter.ConvertZipAsync(zipStream, resultsChannel);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("Zip stream is not seekable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertZipAsync_TooManyEntries_Then_InvalidDataExceptionThrown()
    {
        // Create a zip stream with more than the maximum allowed number of entries (from configuration)
        var maxEntries = _baseTest.GptChatGrxmlTestConfiguration.MaxEntryCount;
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            for (var i = 0; i < maxEntries + 2; i++)
            {
                var entry = archive.CreateEntry($"timeout5000_{i}.grxml");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write("<root>test</root>");
            }
        }
        zipStream.Position = 0;

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await _converter.ConvertZipAsync(zipStream, resultsChannel));

        Assert.Equal("Zip file has too many entries.", ex.Message);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("too many entries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertZipAsync_TooLargeFile_Then_InvalidDataExceptionThrown()
    {
        // PSEUDOCODE:
        // 1. Clear logger.
        // 2. Set a deliberately small MaxEntrySize in test configuration (e.g., 128 bytes).
        // 3. Create a refreshed converter instance using Options.Create so new size is picked up (original _converter captured old options).
        // 4. Build content that exceeds the configured MaxEntrySize by 1 byte.
        // 5. Create an in‑memory zip with a single entry containing that oversized content.
        // 6. Create bounded results channel.
        // 7. Invoke ConvertZipAsync on refreshed converter and assert InvalidDataException is thrown.
        // 8. Assert exception message indicates entry size violation.
        // 9. Assert logs contain size violation message.

        // Set small size to avoid large allocations and force violation quickly.
        var saveMaxEntrySize = _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize;
        _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize = 128; // bytes

        // Create a refreshed converter so new MaxEntrySize is honored.
        var refreshedConverter = new GptChatGrxmlToMcsConverter(
            Options.Create(_baseTest.GptChatGrxmlTestConfiguration),
            _converter.OpenAIChatService,
            _converter.AiContentValidator,
            _converter.TokenValidator);

        var oversizedLength = _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize + 1000;
        var largeContent = new string('A', (int)oversizedLength);

        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("tooLarge.grxml", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write(largeContent);
        }
        zipStream.Position = 0;

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(
            _baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            refreshedConverter.ConvertZipAsync(zipStream, resultsChannel));

        Assert.True(
            ex.Message.Contains("entry", StringComparison.OrdinalIgnoreCase) &&
            ex.Message.Contains("too", StringComparison.OrdinalIgnoreCase) &&
            ex.Message.Contains("large", StringComparison.OrdinalIgnoreCase),
            $"Unexpected exception message: {ex.Message}");

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m =>
            m.Contains("entry", StringComparison.OrdinalIgnoreCase) &&
            m.Contains("size", StringComparison.OrdinalIgnoreCase));
        _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize = saveMaxEntrySize; // restore original value
    }

    [Fact]
    public async Task When_ConvertZipAsync_TooLargeUncompressedZip_Then_InvalidDataExceptionThrown()
    {
        // Configure limits so that individual entries are fine but total uncompressed size exceeds the limit.
        // Because _converter was created in the test fixture ctor (before we change the config here),
        // we must create a new converter instance with the updated configuration; otherwise the old
        // instance still holds the previous configuration snapshot from IOptions.
        var saveMaxEntrySize = _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize;
        _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize = 10_000; // large enough to not trigger single entry violation
        var saveMaxTotalUncompressedSize = _baseTest.GptChatGrxmlTestConfiguration.MaxTotalUncompressedSize;
        _baseTest.GptChatGrxmlTestConfiguration.MaxTotalUncompressedSize = 100; // very small total limit

        var refreshedConverter = new GptChatGrxmlToMcsConverter(
            Options.Create(_baseTest.GptChatGrxmlTestConfiguration),
            _converter.OpenAIChatService,
            _converter.AiContentValidator,
            _converter.TokenValidator);

        // Create multiple small entries whose combined size exceeds MaxTotalUncompressedSize
        var entryContent = new string('A', 60); // each 60 bytes (ASCII)
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            for (var i = 0; i < 3; i++) // 3 * 60 = 180 > 100 (total limit)
            {
                var entry = archive.CreateEntry($"file{i}.grxml", CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
                writer.Write(entryContent);
            }
        }
        zipStream.Position = 0;

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(
            _baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            refreshedConverter.ConvertZipAsync(zipStream, resultsChannel));

        // Flexible assertion: ensure message indicates total size violation
        Assert.True(
             ex.Message.Contains("decompressed", StringComparison.OrdinalIgnoreCase) &&
            ex.Message.Contains("large", StringComparison.OrdinalIgnoreCase),
            $"Unexpected exception message: {ex.Message}");

        _baseTest.GptChatGrxmlTestConfiguration.MaxTotalUncompressedSize = saveMaxTotalUncompressedSize; // restore original value
        _baseTest.GptChatGrxmlTestConfiguration.MaxEntrySize = saveMaxEntrySize; // restore original value

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m =>
            (m.Contains("total", StringComparison.OrdinalIgnoreCase) ||
             m.Contains("uncompressed", StringComparison.OrdinalIgnoreCase)) &&
            m.Contains("size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_ConvertZipAsync_TotalTimeout_Then_ErrorIsLogged()
    {
        var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
        {
            for (var i = 0; i < 10; i++)
            {
                var entry = archive.CreateEntry($"timeout4000_{i}.grxml");
                using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    writer.Write("<root>timeout</root>");
                }
            }
        }
        zipStream.Position = 0;

        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_baseTest.GptChatGrxmlTestConfiguration.ResultStreamChannelCapacity);
        var result = await _converter.ConvertZipAsync(zipStream, resultsChannel);

        Assert.NotNull(result);
        Assert.Equal("Conversion complete", result);
        Assert.Equal(7, resultsChannel.Reader.Count);

        var logMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logMessages, m => m.Contains("Cancelled | Reason=TotalTimeout", StringComparison.OrdinalIgnoreCase));
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
        Assert.Contains("Can't parse this XML", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void When_HashHelper_with_EmptyString_Then_Exception()
    {
        var emptyString = string.Empty;
        Assert.Throws<ArgumentException>(() => HashHelper.HashSha256Hex(emptyString));

        Assert.Throws<ArgumentException>(() => HashHelper.HashSha256Hex(null!));
    }

    [Fact]
    public async Task When_ConvertFileAsync_WithinTokenLimit_Then_ProcessesSuccessfully()
    {
        // Use the existing TestValidXml which should be well within the token limit
        var result = await _converter.ConvertFileAsync(TestValidXml);

        Assert.NotNull(result);
        Assert.DoesNotContain("Error: Token count", result);
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
    // ~GptChatGrxmlToMcsConverterTest()
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
