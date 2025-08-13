using System;
using System.Linq;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Middleware;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs;

public static class Program
{
    private static readonly string[] UrlsFallback = ["configured ASPNETCORE_URLS / hosting defaults"];

    public static WebApplication? App { get; private set; }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var env = builder.Environment;

        bool stubsEnabled = env.IsDevelopment() ||
                            builder.Configuration.GetValue("Stubs:Enabled", false);

        builder.Logging
            .AddConsole()
            .AddConfiguration(builder.Configuration.GetSection("Logging"));

        if (env.IsDevelopment())
        {
            builder.Services.AddHttpLogging(o => o.LoggingFields = HttpLoggingFields.All);
        }

        if (stubsEnabled)
        {
            var chatControllerAssembly = typeof(ChatController).Assembly;
            builder.Services.AddControllers()
                   .PartManager.ApplicationParts.Add(new AssemblyPart(chatControllerAssembly));

            builder.Services.AddTransient<ChatGptService>();

            builder.Services.AddOptions<StubBehaviorOptions>()
                .Bind(builder.Configuration.GetSection(StubBehaviorOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            builder.Services.AddSingleton(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<StubBehaviorOptions>>().Value;
                return new StubBehaviorState(opts);
            });

            builder.Services.AddSingleton<IStubResultCorruptor, JsonResultCorruptor>();
        }

        builder.WebHost.ConfigureKestrel(options =>
        {
            if (env.IsDevelopment())
            {
                options.ListenLocalhost(5000);
                options.ListenLocalhost(5001, lo => lo.UseHttps());
            }
        });

        var app = builder.Build();
        App = app;

        if (env.IsDevelopment()) app.UseHttpLogging();

        app.UseRouting();

        if (stubsEnabled)
        {
            app.UseMiddleware<RequestLoggingMiddleware>();
            app.UseMiddleware<GptStubBehaviorMiddleware>();
            app.MapControllers();
        }
        else
        {
            app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        }

        var urls = string.Join(", ", app.Urls.Count > 0 ? app.Urls : UrlsFallback);
        app.Logger.LogInformation("Stubs {State} | Environment: {Env} | Listening on: {Urls}",
            stubsEnabled ? "ENABLED" : "DISABLED",
            env.EnvironmentName,
            urls);

        app.Run();
    }

    public static async Task StopAsync()
    {
        if (App is null) return;
        await App.StopAsync(TimeSpan.FromSeconds(5));
        await App.DisposeAsync();
        App = null;
    }

    public static void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}