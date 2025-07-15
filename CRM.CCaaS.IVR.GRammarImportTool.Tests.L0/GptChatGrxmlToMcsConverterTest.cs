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

<rule id=""MAIN"" scope=""public"">
<ruleref special=""GARBAGE""/> title     </rule>
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
            _baseTest.ServiceProvider.GetRequiredService<ILogger<GptChatGrxmlToMcsConverter>>(),
            _baseTest.ServiceProvider.GetRequiredService<IOptions<GptChatGrxmlConfiguration>>());
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

    [Theory]
    [InlineData(null, "test-deployment", "test-key")]
    [InlineData("https://test.openai.azure.com/", null, "test-key")]
    [InlineData("https://test.openai.azure.com/", "test-deployment", null)]
    public void When_CreateChatClient_InvalidParameters_Then_ThrowsArgumentNullException(string? endpoint, string? deployment, string? key)
    {
        Assert.Throws<ArgumentException>(() =>
            _converter.CreateChatClient(endpoint, deployment, key));
    }

    [Fact]
    public void When_CreateChatClient_ValidParameters_Then_ReturnsChatClient()
    {
        string endpoint = "https://test.openai.azure.com/";
        string deployment = "test-deployment";
        string key = "test-key";

        var chatClient = _converter.CreateChatClient(endpoint, deployment, key);

        Assert.NotNull(chatClient);
        Assert.IsAssignableFrom<IChatClient>(chatClient);
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
