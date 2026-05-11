// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
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
using Microsoft.AspNetCore.HttpsPolicy;
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
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly())
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "GPTPrompter")
            .AddCommandLine(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        startUpLogger.LogInformation("Configuring SSL and Listening Ports...");
        var httpPort = builder.Configuration.GetValue<int>("Main:HttpPlainTextPort");
        var httpsPort = builder.Configuration.GetValue<int>("Main:HttpSslPort");
        ConfigureListeningPortsProtocolsCertsAndLimits(builder, httpPort, httpsPort, startUpLogger);

        // HSTS policy: 365-day max-age and includeSubDomains. The header is
        // only emitted in the non-Test/Dev pipeline branch below.
        builder.Services.Configure<HstsOptions>(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        // HTTPS redirection target: the middleware otherwise cannot infer the
        // HTTPS port from Kestrel's ListenAnyIP endpoints and falls back to
        // 443, producing broken redirects. Pin it to the configured HTTPS port
        // (and skip wiring redirection when no HTTPS port is configured).
        if (httpsPort >= 0 && httpsPort <= 65535)
        {
            builder.Services.Configure<HttpsRedirectionOptions>(options =>
            {
                options.HttpsPort = httpsPort;
            });
        }

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

        // Surface a loud, non-blocking warning when the deployment is configured
        // to authenticate to Azure OpenAI with a static API key in a non-dev /
        // non-test environment. The audit guidance is that production should
        // use EntraID / Managed Identity; this log gives operators a clear
        // signal without breaking existing deployments. The warning is suppressed
        // when AzureOpenAIKey is empty because in that case the factory silently
        // promotes the credential to DefaultAzureCredential (and logs a separate
        // warning of its own), so the effective auth mode is no longer ApiKey.
        var gptChatConfig = builder.Configuration.GetSection(GptChatGrxmlConfiguration.SectionName)
            .Get<GptChatGrxmlConfiguration>();
        if (gptChatConfig != null
            && string.Equals(gptChatConfig.OpenAI_Provider, GptChatGrxmlConfiguration.OpenAIProvider_AzureOpenAI, StringComparison.OrdinalIgnoreCase)
            && string.Equals(gptChatConfig.AzureOpenAIAuthMode, GptChatGrxmlConfiguration.AzureOpenAIAuthMode_ApiKey, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(gptChatConfig.AzureOpenAIKey)
            && !builder.Environment.IsTestOrDev())
        {
            startUpLogger.LogWarning(
                "AzureOpenAIAuthMode=ApiKey in environment '{Environment}'. Static API keys are discouraged for production. Set GptChat:Grxml:AzureOpenAIAuthMode to 'ManagedIdentity' (recommended) or 'DefaultAzureCredential' and grant the workload's identity the 'Cognitive Services OpenAI User' role on the Azure OpenAI resource. See docs/security-posture.md for details.",
                builder.Environment.EnvironmentName);
        }
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

        // Transport encryption: in non-dev/non-test environments, advertise
        // HSTS so browsers refuse plain-HTTP to this host for the configured
        // max-age (365 days; configured above on HstsOptions) and upgrade any
        // incoming HTTP request to HTTPS using the explicit HttpsRedirection
        // target port. Test/Dev keep plain HTTP available so integration tests
        // and the local stub can still talk to the service over http://localhost.
        if (!app.Environment.IsTestOrDev())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
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
                        // Constrain Kestrel to TLS 1.2 / TLS 1.3 only. Older
                        // protocol versions (SSL 3, TLS 1.0, TLS 1.1) are
                        // disallowed by the transport-encryption policy. The
                        // actual cipher suite (ECDHE-based with NIST P-256/P-384
                        // curves) is selected from the OS-level TLS stack:
                        //   - Linux: managed via CipherSuitesPolicy / OpenSSL config
                        //   - Windows: managed via SCHANNEL policy
                        // and is the deployment platform's responsibility (App
                        // Gateway / Ingress / OS). See docs/security-posture.md.
                        //
                        // CA5398 suggests SslProtocols.None to let the OS pick a
                        // version. The audit policy here is the opposite:
                        // protocols MUST be pinned explicitly so a future OS /
                        // runtime can never silently re-enable TLS 1.0 / 1.1.
                        // Suppression is intentional and reviewed.
#pragma warning disable CA5398
                        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
#pragma warning restore CA5398

                        if (builder.Configuration.GetValue("Main:UseSelfSignedCertificate", true))
                        {
                            logger.LogInformation("Using one-time self-signed certificate for HTTPS (dev/demo only).");
                            httpsOptions.ServerCertificate = CreateTempCerts();
                        }
                        else
                        {
                            // Production cert loading via the standard ASP.NET Core
                            // convention: a PKCS#12 (.pfx) file pointed at by
                            // Kestrel:Certificates:Default:Path with an optional
                            // Password. Operators provide both via
                            // appsettings.{Environment}.json or environment variables
                            // (Kestrel__Certificates__Default__Path,
                            // Kestrel__Certificates__Default__Password). Anything more
                            // sophisticated (Key Vault, k8s secret hot-reload) is an
                            // adapter — see docs/security-posture.md → Transport encryption.
                            var certPath = builder.Configuration["Kestrel:Certificates:Default:Path"];
                            var certPassword = builder.Configuration["Kestrel:Certificates:Default:Password"];
                            if (string.IsNullOrWhiteSpace(certPath))
                            {
                                throw new InvalidOperationException(
                                    "Main:UseSelfSignedCertificate is false but Kestrel:Certificates:Default:Path is not configured. " +
                                    "Provide a PKCS#12 certificate path (and optional Password) in configuration, " +
                                    "or revert to UseSelfSignedCertificate=true for dev/demo. " +
                                    "See docs/security-posture.md → Transport encryption for the production setup.");
                            }
                            logger.LogInformation("Loading HTTPS certificate from {CertPath} (Kestrel:Certificates:Default).", certPath);
                            // Always use the PFX overload (empty/null password is valid for
                            // unprotected files). HasPrivateKey is required to serve TLS —
                            // fail-fast with an explicit message if the file lacks one,
                            // instead of letting Kestrel emit an opaque handshake error.
                            var cert = new X509Certificate2(certPath, certPassword ?? string.Empty);
                            if (!cert.HasPrivateKey)
                            {
                                throw new InvalidOperationException(
                                    $"HTTPS certificate at '{certPath}' does not contain a private key. " +
                                    "Re-export as PKCS#12 including the private key, or point Kestrel:Certificates:Default:Path at the correct file.");
                            }
                            httpsOptions.ServerCertificate = cert;
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
