using System.Reflection;
using System.Runtime;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Endpoints;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Main;

public static class Program
{
    public static WebApplication? MainApp;
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("AppSettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .AddJsonFile($"AppSettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "GPTPrompter")
            .AddCommandLine(args);

        builder.Logging.AddConsole()
            .AddConfiguration(builder.Configuration.GetSection("Logging"));

        var httpPort = builder.Configuration.GetValue<int>("Main:HttpPlainTextPort");
        var httpsPort = builder.Configuration.GetValue<int>("Main:HttpSslPort");
        ConfigureListeningPortsProtocolsCertsAndLimits(builder, httpPort, httpsPort);

        builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        builder.Services.AddControllers().AddApplicationPart(typeof(GrITController).Assembly);

        // Add services to the container.
        builder.Services.Configure<GptChatGrxmlConfiguration>(builder.Configuration.GetSection(GptChatGrxmlConfiguration.SectionName));

        builder.Services.AddProblemDetails();
        builder.Services.AddKeyedTransient<IGptChat, GptChatGrxmlToMcsConverter>(GptChatGrxmlToMcsConverter.SERVICE_KEY);

        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
        })
        .AddHubOptions<GritHub>(hubOptions =>
        {
            hubOptions.MaximumReceiveMessageSize = 5 * 1024 * 1024; // 1 MB
        });

        // Add CORS policy for SignalR/WebSockets
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("DevOrTest", builder =>
            {
                builder
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .SetIsOriginAllowed(_ => true) // Allow any origin for development or test
                    .AllowCredentials();
            });
        });
        var corsOrigins = builder.Configuration.GetSection("Main:Cors:AllowedOrigins").Get<string[]>();
        if (corsOrigins == null || corsOrigins.Length == 0)
        {
            corsOrigins = ["https://localhost:8443"];
        }
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("Production", builder =>
            {
                builder
                    .AllowAnyHeader()
                    .WithMethods("GET", "POST")
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .WithOrigins(corsOrigins)
                    .AllowCredentials();
            });
        });

        var app = builder.Build();
        app.UseRouting();
        app.MapControllers();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Main");

        if (app.Environment.IsTestOrDev())
        {
            logger.LogInformation("Application started in development or test environment at {Timestamp}.", DateTime.UtcNow);
            app.UseCors("DevOrTest"); // Use CORS policy for development or test
        }
        else
        {
            logger.LogInformation("Application started in production environment at {Timestamp}.", DateTime.UtcNow);
            app.UseCors("Production"); // Use CORS policy for production
        }

        // Configure the HTTP request pipeline.
        app.UseExceptionHandler();

        //if (app.Environment.IsDevelopment())
        //{
        //    app..MapOpenApi();
        //}
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.MapGritEndpoints();

        logger.LogInformation("Application about to start at {Timestamp}.", DateTime.UtcNow);

        MainApp = app;
        MainApp.Run(); // only blocks in real run
        MainApp.DisposeAsync().AsTask().Wait(); // Dispose the app gracefully
    }

    private static void ConfigureListeningPortsProtocolsCertsAndLimits(WebApplicationBuilder builder, int httpPort, int httpsPort)
    {
        var isDevelopment = builder.Environment.IsDevelopment();

        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxConcurrentUpgradedConnections = builder.Configuration.GetValue("SporchMain:MaxConcurrentUpgradedConnections", 500);
            options.Limits.MaxConcurrentConnections = builder.Configuration.GetValue("SporchMain:MaxConcurrentConnections", 500);
            options.Limits.Http2.MaxStreamsPerConnection = builder.Configuration.GetValue("SporchMain:Http2MaxStreamsPerConnection", 100);

            if (httpPort >= 0 && httpPort <= 65535)
            {
                options.ListenAnyIP(httpPort, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1;
                });
            }

            if (httpsPort >= 0 && httpsPort <= 65535)
            {
                options.ListenAnyIP(httpsPort, listenOptions =>
                {
                    listenOptions.UseHttps(httpsOptions =>
                    {
                        if (!IsTestOrDev(builder.Environment))
                        {
                            //var certificateReloadService = options.ApplicationServices.GetRequiredKeyedService<CertificateReloadService>("tls-grit-svc-cluster-local");
                            //httpsOptions.ServerCertificateSelector = (context, name) => certificateReloadService.CurrentCertificate();
                        }
                    });
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                });
            }
        });
    }

    private static bool IsTestOrDev(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsEnvironment("Test") || environment.IsEnvironment("Local") || environment.IsEnvironment("Development");
    }
}
