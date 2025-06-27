using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Xunit;
using Stubs = CRM.CCaaS.IVR.GRammarImportTool.Stubs;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L1;

public class IntegrationTest : IClassFixture<TestD>, IDisposable
{
    private bool _disposedValue;
    private readonly TestD _testD;
    private readonly ITestOutputHelper _output;
    private readonly TestConsoleWriter _converter;
    private const string SIGNALR_GRIT_HUB = "grithub";

    public IntegrationTest(TestD testD, ITestOutputHelper output)
    {
        _testD = testD;
        _output = output;

        _converter = new TestConsoleWriter(_output);
        Console.SetOut(_converter);
    }

    protected void PrintFilesInBase64Zip(string base64Zip)
    {
        byte[] zipBytes = Convert.FromBase64String(base64Zip);

        using (var zipStream = new MemoryStream(zipBytes))
        {
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                _output.WriteLine("Files in ZIP archive:");
                foreach (var entry in archive.Entries)
                {
                    _output.WriteLine($"- {entry.FullName}");

                    using (var entryStream = entry.Open())
                    using (var reader = new StreamReader(entryStream, Encoding.UTF8))
                    {
                        string content = reader.ReadToEnd();
                        _output.WriteLine(content);
                    }
                }
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // _httpClient.Dispose();
                _converter.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task When_app_started_Then_healthcheck_success()
    {
        var response = await _testD.GetHttpClient().GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("grit-test-data-1.zip")]
    [InlineData("grit-test-data-2.zip")]
    public async Task When_upload_zip_file_to_hub_Then_conversion_success(string zipFileName)
    {
        var connection = new HubConnectionBuilder()
                    .WithUrl($"{_testD.GetHttpClient().BaseAddress}{SIGNALR_GRIT_HUB}")
                    .ConfigureLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                        logging.AddConsole();
                    })
                    .Build();
        connection.On<string>("ConnectionId", connectionId =>
        {
            _output.WriteLine($"ConnectionId received: {connectionId}");
        });

        connection.On<int, string>("Progress", (progress, message) =>
        {
            _output.WriteLine($"Progress: {progress}% - Message: {message}");
        });

        connection.On<string>("Completed", (resultBase64) =>
        {
            _output.WriteLine($"Upload completed with result: {resultBase64}");
            PrintFilesInBase64Zip(resultBase64);
        });

        connection.On<string>("Error", error =>
        {
            _output.WriteLine($"Error: {error}");
        });

        await connection.StartAsync();
        _output.WriteLine("Connection started.");

        Assert.True(connection.State == HubConnectionState.Connected, "Connection should be disconnected after the test.");

        byte[] zipFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", zipFileName));
        await connection.InvokeAsync("UploadZipFile", zipFileBytes);
        _output.WriteLine("File upload initiated.");

        await connection.StopAsync();
        _output.WriteLine("Connection stopped.");

        Assert.True(connection.State == HubConnectionState.Disconnected, "Connection should be disconnected after the test.");
        Assert.Equal(0, _converter.GetLines().Count(x => x.Contains("Error:", StringComparison.Ordinal)));
    }
}
