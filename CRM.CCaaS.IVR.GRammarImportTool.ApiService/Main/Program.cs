using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Endpoints;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Middleware;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Main;

[ExcludeFromCodeCoverage]
public static class Program
{
    public static WebApplication? MainApp;
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var startUpLogger = CreateStartUpLogger();
        startUpLogger.LogInformation("Starting up GrIT API Service...");

        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddEndpointsApiExplorer();
        }
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("AppSettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .AddJsonFile($"AppSettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "GPTPrompter")
            .AddCommandLine(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        startUpLogger.LogInformation("Configuring SSL and Listening Ports...");
        var httpPort = builder.Configuration.GetValue<int>("Main:HttpPlainTextPort");
        var httpsPort = builder.Configuration.GetValue<int>("Main:HttpSslPort");
        ConfigureListeningPortsProtocolsCertsAndLimits(builder, httpPort, httpsPort, startUpLogger);

        startUpLogger.LogInformation("Configuring MVC, Swagger, and Application Parts...");
        builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        builder.Services.AddControllers().AddApplicationPart(typeof(GrITController).Assembly);

        // Add services to the container.
        builder.Services.Configure<GptChatGrxmlConfiguration>(builder.Configuration.GetSection(GptChatGrxmlConfiguration.SectionName));

        builder.Services.AddProblemDetails();

        builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        builder.Services.AddSingleton<JobTracker>();
        builder.Services.AddKeyedSingleton<IConversionResultsStore, InMemoryConversionResultsStore>(InMemoryConversionResultsStore.SERVICE_KEY);
        builder.Services.AddKeyedSingleton<IConversionResultsStore, FileConversionResultsStore>(FileConversionResultsStore.SERVICE_KEY);
        builder.Services.AddHostedService<GrxmlConversionService>();

        builder.Services.AddSingleton<IAzureOpenAIClientFactory, AzureOpenAIClientFactory>();
        builder.Services.AddSingleton(provider =>
        {
            var config = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GptChatGrxmlConfiguration>>();
            var azureFactory = provider.GetRequiredService<IAzureOpenAIClientFactory>();
            return ChatServiceFactory.Create(config, azureFactory);
        });
        // Add file validation and AI content validation services
        builder.Services.AddTransient<FileValidator>();
        builder.Services.AddTransient<AiContentValidator>();
        builder.Services.AddTransient<TokenValidator>();

        builder.Services.AddKeyedTransient<IGptChat, GptChatGrxmlToMcsConverter>(GptChatGrxmlToMcsConverter.SERVICE_KEY);

        startUpLogger.LogInformation("Adding SignalR");
        builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
        })
        .AddHubOptions<GrITHub>(hubOptions =>
        {
            hubOptions.MaximumReceiveMessageSize = builder.Configuration.GetValue("GptChat:Grxml:MaxSignalRMessageSizeBytes", 5 * 1024 * 1024); // Default to 5 MB);
        });

        startUpLogger.LogInformation("Configuring CORS policies for SignalR/WebSockets");
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

        if (app.Environment.IsDevelopment())
        {

            var swaggerPath = Path.Combine(builder.Environment.ContentRootPath, "ApiDefinitions");

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(swaggerPath),
                RequestPath = "/swagger"
            });
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/GrITOpenAPI.json", "GrIT API");
                c.RoutePrefix = "swagger";
            });
        }

        app.UseMiddleware<ExceptionMiddleware>();
        app.UseRouting();
        app.MapControllers();

        if (GrITLoggerFactory.Instance == null)
        {

            var logFactory = app.Services.GetRequiredService<ILoggerFactory>();
            GrITLoggerFactory.Instance = logFactory;
        }
        var logger = GrITLoggerFactory.Instance.CreateLogger("Main");

        if (app.Environment.IsTestOrDev())
        {
            app.Lifetime.ApplicationStopping.Register(() =>
            {
                logger.LogInformation("Application is stopping gracefully.");
            });

            app.Lifetime.ApplicationStopped.Register(() =>
            {
                logger.LogInformation("Application has stopped.");
            });
            logger.LogInformation("Application started in development or test environment.");
            app.UseCors("DevOrTest"); // Use CORS policy for development or test
        }
        else
        {
            logger.LogInformation("Application started in production environment.");
            app.UseCors("Production"); // Use CORS policy for production
        }

        // Configure the HTTP request pipeline.
        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.MapGrITEndpoints();

        logger.LogInformation(@" ________      ._____________");
        logger.LogInformation(@"/  _____/______|__\__    ___/");
        logger.LogInformation(@"/   \  __\_  __ \  | |    |  ");
        logger.LogInformation(@"\    \_\  \  | \/  | |    |  ");
        logger.LogInformation(@" \______  /__|  |__| |____|  ");
        logger.LogInformation(@"        \/                   ");

        logger.LogInformation("GriT is about to start.");

        MainApp = app;
        MainApp.Run(); // only blocks in real run
        MainApp.DisposeAsync().AsTask().Wait(); // Dispose the app gracefully
    }

    private static ILogger CreateStartUpLogger()
    {
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        ILogger logger = factory.CreateLogger("StartUp");
        return logger;
    }

    private static void ConfigureListeningPortsProtocolsCertsAndLimits(WebApplicationBuilder builder, int httpPort, int httpsPort, ILogger logger)
    {
        var isDevelopment = builder.Environment.IsDevelopment();

        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxConcurrentUpgradedConnections = builder.Configuration.GetValue("Main:MaxConcurrentUpgradedConnections", 500);
            options.Limits.MaxConcurrentConnections = builder.Configuration.GetValue("Main:MaxConcurrentConnections", 500);

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
                        if (builder.Configuration.GetValue("Main:UseSelfSignedCertificate", true))
                        {
                            logger.LogInformation("Using one time self-signed certificate for HTTPS.");
                            httpsOptions.ServerCertificate = CreateTempCerts();
                        }
                        else
                        {
                            //var certificateReloadService = options.ApplicationServices.GetRequiredKeyedService<CertificateReloadService>("tls-grit-svc-cluster-local");
                            //httpsOptions.ServerCertificateSelector = (context, name) => certificateReloadService.CurrentCertificate();
                        }
                    });
                });
            }
        });
    }

    private static bool IsTestOrDev(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsEnvironment("Test") || environment.IsEnvironment("Local") || environment.IsEnvironment("Development");
    }

    private static X509Certificate2 CreateTempCerts()
    {
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");

        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest(
                "CN=GrIT",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DataEncipherment |
                X509KeyUsageFlags.KeyEncipherment |
                X509KeyUsageFlags.DigitalSignature,
                critical: false));

            request.CertificateExtensions.Add(sanBuilder.Build());

            var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-45),
                DateTimeOffset.UtcNow.AddDays(365));

            byte[] pfxBytes = cert.Export(X509ContentType.Pkcs12, "");

            var persistedCert = new X509Certificate2(
                pfxBytes,
                "",
                X509KeyStorageFlags.PersistKeySet);
            return persistedCert;

        }
    }
}
