using System.IO.Compression;
using System.Security.Cryptography;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L1.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L1;

public class BaseTest : IDisposable
{
    public TestLoggerProvider LogProvider { get; private set; } = new TestLoggerProvider();

    private bool _disposedValue;
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly List<string> _events = new List<string>();
    private readonly Task _stub;
    private readonly Task _main;
    private readonly CancellationTokenSource _testCancelationTokenSource = new CancellationTokenSource();
    private static readonly string[] Args = ["--environment=Test"];

    public BaseTest()
    {
        _stub = Task.Run(() =>
        {
            Stubs.Program.Main([""]);

        }, _testCancelationTokenSource.Token);

        Console.WriteLine($"Stub application started. {_stub.Id}");

        _main = Task.Run(() =>
        {
            var logger = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(LogProvider);
            });
            ApiService.Util.Logging.GrITLoggerFactory.Instance = logger;
            ApiService.Main.Program.Main(Args);
        });

        do
        {
            Task.Delay(1000).Wait();
        } while (ApiService.Main.Program.MainApp == null);

        _handler = new HttpClientHandler();
        _handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        _handler.ServerCertificateCustomValidationCallback =
            (httpRequestMessage, cert, cetChain, policyErrors) =>
            {
                Console.WriteLine("Trust test certificate");
                return true;
            };
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("http://localhost:5003")
        };
        _httpClient.Timeout = TimeSpan.FromMinutes(30);

        Console.WriteLine($"Main application started. {_main.Id}");
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
                _handler.Dispose();
                _testCancelationTokenSource.Cancel();
                Stubs.Program.Stop();
                _stub.Wait(TimeSpan.FromSeconds(10));
                _testCancelationTokenSource.Dispose();
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
