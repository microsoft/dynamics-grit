using System.Collections.Concurrent;
using System.Net;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs;

public static class Program
{
    public static WebApplication? App { get; private set; }

    public static ConcurrentDictionary<string, string> PendingErrors = new();
    public const string PendingErrorSeparator = "#";

    public static KeyValuePair<string, string>? GetPendingError()
    {
        foreach (var error in PendingErrors)
        {
            if (PendingErrors.TryRemove(error.Key, out var removedError))
            {
                return new KeyValuePair<string, string>(error.Key, removedError);
            }
        }
        return null;
    }

    public static void AddPendingError(string key, string error)
    {
        var uniqueKey = $"{key}{PendingErrorSeparator}{Guid.NewGuid()}";
        PendingErrors[uniqueKey] = error;
    }

    public static void ClearPendingErrors()
    {
        PendingErrors.Clear();
    }

    public static void Main(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Local";
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddHttpLogging(logging =>
        {
            logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
        });

        builder.Logging.AddConsole()
            .AddConfiguration(builder.Configuration.GetSection("Logging"));

        var chatControllerAssembly = typeof(ChatController).Assembly;
        builder.Services.AddControllers().PartManager.ApplicationParts.Add(new AssemblyPart(chatControllerAssembly));

        builder.Services.AddTransient<ChatGptService>();

        builder.WebHost.ConfigureKestrel(listenOptions =>
        {
            var ipAddress = environment == "Production" ? "127.0.0.1" : "0.0.0.0";
            listenOptions.Listen(IPAddress.Parse(ipAddress), 5001);
        });

        App = builder.Build();

        var loggerFactory = App.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("CRM.CCaaS.IVR.GRammarImportTool.Stubs");
        App.UseMiddleware<RequestLoggingMiddleware>();

        logger.LogInformation("Starting CRM.CCaaS.IVR.GRammarImportTool.Stubs on port 5001");
        App.UseRouting();
        App.MapControllers();
        App.Run();
    }

    public static async Task Stop()
    {
        if (App == null)
            return;
        await App.StopAsync(TimeSpan.FromSeconds(5));
        App.DisposeAsync().AsTask().Wait();
        App = null;
    }
}
