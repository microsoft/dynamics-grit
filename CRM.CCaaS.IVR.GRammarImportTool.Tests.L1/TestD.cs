using System.IO.Compression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Xunit.Abstractions;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L1;

public class TestD : IDisposable
{
    private bool _disposedValue;
    private readonly HttpClient _httpClient;
    private readonly Aspire.Hosting.DistributedApplication _app;
    private readonly List<string> _events = new List<string>();
    private readonly Task _stub;
    private readonly CancellationTokenSource _stubCancelationTokenSource = new CancellationTokenSource();
    private static readonly string[] Args = ["--environment=Test"];

    public TestD()
    {
        _stub = Task.Run(() =>
        {
            Stubs.Program.Main([""]);

        }, _stubCancelationTokenSource.Token);

        Console.WriteLine($"Stub application started. {_stub.Id}");

        // Arrange
        var appHost = DistributedApplicationTestingBuilder.CreateAsync<Projects.CRM_CCaaS_IVR_GRammarImportTool_AppHost>(Args).Result;
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });
        // To output logs to the xUnit.net ITestOutputHelper, consider adding a package from https://www.nuget.org/packages?q=xunit+logging

        _app = appHost.Build();
        var resourceNotificationService = _app.Services.GetRequiredService<ResourceNotificationService>();
        _app.StartAsync().Wait();

        // Act
        _httpClient = _app.CreateHttpClient("apiservice");
        resourceNotificationService.WaitForResourceAsync("apiservice", KnownResourceStates.Running).Wait(TimeSpan.FromSeconds(30));
    }

    public HttpClient GetHttpClient()
    {
        return _httpClient;
    }

    public void AddEvent(string eventName)
    {
        _events.Add(eventName);
    }

    public IReadOnlyList<string> GetEvents()
    {
        return _events.AsReadOnly();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _httpClient.Dispose();
                _app.Dispose();
                _stubCancelationTokenSource.Cancel();
                Stubs.Program.Stop();
                _stub.Wait(TimeSpan.FromSeconds(10));
                _stubCancelationTokenSource.Dispose();
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~TestD()
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
