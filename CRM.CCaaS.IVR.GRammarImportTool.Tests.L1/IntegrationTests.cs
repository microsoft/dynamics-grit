using System;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Main;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L1.Util;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Stubs = CRM.CCaaS.IVR.GRammarImportTool.Stubs;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L1;

public class IntegrationTests : IClassFixture<BaseTest>, IDisposable
{
    private bool _disposedValue;
    private readonly BaseTest _baseTest;
    private readonly ITestOutputHelper _output;
    private readonly TestConsoleWriter _converter;
    private const string SIGNALR_GRIT_HUB = "grithub";

    public IntegrationTests(BaseTest testD, ITestOutputHelper output)
    {
        _baseTest = testD;
        _output = output;

        _converter = new TestConsoleWriter(_output);
        Console.SetOut(_converter);
        _converter.ClearLines();
    }

    protected void PrintFilesInBase64Zip(string base64Zip)
    {
        byte[] zipBytes = Convert.FromBase64String(base64Zip);

        using (var zipStream = new MemoryStream(zipBytes))
        {
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                _converter.WriteLine("Files in ZIP archive:");
                foreach (var entry in archive.Entries)
                {
                    _converter.WriteLine($"- {entry.FullName}");

                    using (var entryStream = entry.Open())
                    using (var reader = new StreamReader(entryStream, Encoding.UTF8))
                    {
                        string content = reader.ReadToEnd();
                        _converter.WriteLine(content);
                    }
                }
            }
        }
    }

    private bool TestForError(IEnumerable<string>? lines)
    {
        if (lines == null)
        {
            return true;
        }
        return lines.Any(x => x.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                              x.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                              x.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _converter.WriteLine("Logged Messages:");
                foreach (var log in _baseTest.LogProvider.Logger.LoggedMessages)
                {
                    _converter.WriteLine($"{log}");
                }
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
        var response = await _baseTest.GetHttpClient().GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("grit-test-data-1.zip")]
    [InlineData("grit-test-data-2.zip")]
    [InlineData("grit-test-data-3.zip")] //includes subfolders with duplicate file names
    public async Task When_upload_zip_file_to_hub_Then_conversion_success(string zipFileName)
    {
        var connection = new HubConnectionBuilder()
                    .WithUrl($"{_baseTest.GetHttpClient().BaseAddress}{SIGNALR_GRIT_HUB}")
                    .ConfigureLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                        logging.AddConsole();
                    })
                    .Build();
        connection.On<string>("ConnectionId", connectionId =>
        {
            _converter.WriteLine($"ConnectionId received: {connectionId}");
        });

        connection.On<int, string>("Progress", (progress, message) =>
        {
            _converter.WriteLine($"Progress: {progress}% - Message: {message}");
        });

        connection.On<string>("Completed", (resultBase64) =>
        {
            _converter.WriteLine($"Upload completed with result: {resultBase64}");
            PrintFilesInBase64Zip(resultBase64);
        });

        connection.On<string>("Error", error =>
        {
            _converter.WriteLine($"Error: {error}");
        });

        await connection.StartAsync();
        _output.WriteLine("Connection started.");

        Assert.True(connection.State == HubConnectionState.Connected, "Connection should be disconnected after the test.");

        byte[] zipFileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", zipFileName));
        await connection.InvokeAsync("GrxmlZipConvert", zipFileBytes);
        _converter.WriteLine("File upload initiated.");

        await connection.StopAsync();
        _converter.WriteLine("Connection stopped.");

        Assert.True(connection.State == HubConnectionState.Disconnected, "Connection should be disconnected after the test.");
        Assert.False(TestForError(_converter.GetLines()));
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("grit-test-data-1.grxml")]
    [InlineData("grit-test-data-2.grxml")]
    public async Task When_upload_grxml_file_to_hub_Then_conversion_success(string fileName)
    {
        var connection = new HubConnectionBuilder()
                    .WithUrl($"{_baseTest.GetHttpClient().BaseAddress}{SIGNALR_GRIT_HUB}")
                    .ConfigureLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Debug);
                        logging.AddConsole();
                    })
                    .Build();
        connection.On<string>("ConnectionId", connectionId =>
        {
            _converter.WriteLine($"ConnectionId received: {connectionId}");
        });

        connection.On<int, string>("Progress", (progress, message) =>
        {
            _converter.WriteLine($"Progress: {progress}% - Message: {message}");
        });

        connection.On<string>("Completed", (result) =>
        {
            _converter.WriteLine($"Upload completed with result: {result}");
        });

        connection.On<string>("Error", error =>
        {
            _converter.WriteLine($"Error: {error}");
        });

        await connection.StartAsync();
        _converter.WriteLine("Connection started.");

        Assert.True(connection.State == HubConnectionState.Connected, "Connection should be disconnected after the test.");

        byte[] fileBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", fileName));
        await connection.InvokeAsync("GrxmlConvert", fileBytes);
        _converter.WriteLine("File upload initiated.");

        await connection.StopAsync();
        _converter.WriteLine("Connection stopped.");

        Assert.True(connection.State == HubConnectionState.Disconnected, "Connection should be disconnected after the test.");
        Assert.False(TestForError(_converter.GetLines()));
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("grit-test-data-1.zip")]
    [InlineData("grit-test-data-2.zip")]
    [InlineData("grit-test-data-3.zip")] //includes subfolders with duplicate file names
    public async Task When_upload_zip_file_to_post_Then_conversion_success(string zipFileName)
    {
        var httpClient = _baseTest.GetHttpClient();
        using var form = new MultipartFormDataContent();

        var fileStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", zipFileName));
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        form.Add(fileContent, "file", Path.GetFileName(zipFileName));

        var response = await httpClient.PostAsync("grit/zip", form);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            _converter.WriteLine(line);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(TestForError(_converter.GetLines()));
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("grit-test-data-1.grxml")]
    [InlineData("grit-test-data-2.grxml")]
    public async Task When_upload_grxml_file_to_post_Then_conversion_success(string grxmlFileName)
    {
        var httpClient = _baseTest.GetHttpClient();
        using var form = new MultipartFormDataContent();

        var fileStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", grxmlFileName));
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/srgs+xml");

        form.Add(fileContent, "file", Path.GetFileName(grxmlFileName));

        var response = await httpClient.PostAsync("grit/grxml", form);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            _converter.WriteLine(line);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(TestForError(_converter.GetLines()));
    }

    [Theory]
    [Trait("Category", "ErrorHandling")]
    [InlineData("grit-test-data-bad-1.grxml")]
    public async Task When_upload_bad_grxml_file_to_post_Then_conversion_fails(string grxmlFileName)
    {
        var httpClient = _baseTest.GetHttpClient();
        using var form = new MultipartFormDataContent();

        var fileStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", grxmlFileName));
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/srgs+xml");

        form.Add(fileContent, "file", Path.GetFileName(grxmlFileName));

        var response = await httpClient.PostAsync("grit/grxml", form);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            _converter.WriteLine(line);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, _converter.GetLines().Count(x => x.Contains("Error:", StringComparison.Ordinal)));
    }

    [Theory]
    [Trait("Category", "ErrorHandling")]
    [InlineData("grit-test-data-bad-1.zip")]
    public async Task When_upload_bad_zip_file_to_post_Then_conversion_fails_for_bad_grxml_only(string zipFileName)
    {
        var httpClient = _baseTest.GetHttpClient();
        using var form = new MultipartFormDataContent();

        var fileStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", zipFileName));
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        form.Add(fileContent, "file", Path.GetFileName(zipFileName));

        var response = await httpClient.PostAsync("grit/zip", form);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            _converter.WriteLine(line);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _converter.GetLines().Count(x => x.Contains("bad string to fail xml parsing", StringComparison.Ordinal)));
    }

    [Theory]
    [Trait("Category", "Load")]
    [InlineData("grit-test-data-1.grxml", 10, 10)]
    public async Task When_mutiple_uploads_grxml_file_to_post_Then_conversion_success(string grxmlFileName,
        int parallelRequests,
        int loops)
    {
        var tasks = new List<Task>();
        var httpClient = _baseTest.GetHttpClient();

        for (int j = 0; j < loops; j++)
        {
            _converter.WriteLine($"Loop {j + 1} of {loops}");
            for (int i = 0; i < parallelRequests; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    string collectedLines = string.Empty;
                    using var form = new MultipartFormDataContent();

                    var fileStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", grxmlFileName));
                    using var fileContent = new StreamContent(fileStream);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/srgs+xml");

                    form.Add(fileContent, "file", Path.GetFileName(grxmlFileName));

                    var response = await httpClient.PostAsync("grit/grxml", form);
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream);
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        collectedLines += line + Environment.NewLine;
                    }
                    _converter.WriteLine(collectedLines);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }));
                await Task.Delay(100);
            }
            await Task.WhenAll(tasks);
            tasks.Clear();
            _converter.WriteLine($"Completed loop {j + 1} of {loops}");
            await Task.Delay(1000);
        }

        Assert.False(TestForError(_converter.GetLines()));
    }

    [Theory]
    [Trait("Category", "Load")]
    [InlineData("grit-test-data-1.zip", 20, 2)]
    public async Task When_mutiple_uploads_zip_file_to_post_Then_conversion_success(string zipFileName,
        int parallelRequests,
        int loops)
    {
        var tasks = new List<Task>();
        var httpClient = _baseTest.GetHttpClient();

        for (int j = 0; j < loops; j++)
        {
            _converter.WriteLine($"Loop {j + 1} of {loops}");
            for (int i = 0; i < parallelRequests; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    string collectedLines = string.Empty;
                    using var form = new MultipartFormDataContent();

                    var fileStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", zipFileName));
                    using var fileContent = new StreamContent(fileStream);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

                    form.Add(fileContent, "file", Path.GetFileName(zipFileName));

                    var response = await httpClient.PostAsync("grit/zip", form);

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream);

                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        collectedLines += line + Environment.NewLine;
                    }
                    _converter.WriteLine(collectedLines);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }));
                await Task.Delay(100);
            }

            await Task.WhenAll(tasks);
            tasks.Clear();
            _converter.WriteLine($"Completed loop {j + 1} of {loops}");
            await Task.Delay(1000);
        }

        Assert.False(TestForError(_converter.GetLines()));
    }
}
