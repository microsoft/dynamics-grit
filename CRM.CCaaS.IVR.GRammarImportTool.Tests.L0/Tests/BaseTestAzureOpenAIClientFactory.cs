using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests;
public class BaseTestAzureOpenAIClientFactory : IDisposable
{
    public TestLoggerProvider LogProvider { get; private set; } = new TestLoggerProvider();
    public IServiceProvider? ServiceProvider { get; private set; }
    public GptChatGrxmlConfiguration GptChatGrxmlTestConfiguration { get; private set; } = new GptChatGrxmlConfiguration();

    private bool _disposedValue;

    public BaseTestAzureOpenAIClientFactory()
    {
        var logger = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(LogProvider);
        });
        ApiService.Util.Logging.GrITLoggerFactory.Instance = logger;

        SetupMocks();
    }

    protected virtual void SetupMocks()
    {
        var services = BuildMocks();
        BuildServiceProvider(services);
    }


    protected void BuildServiceProvider(ServiceCollection services)
    {
        ServiceProvider = services.BuildServiceProvider();
    }

    protected ServiceCollection BuildMocks()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new UnitTestHostEnvironment { EnvironmentName = "Development" });
        services.AddTransient<IAzureOpenAIClientFactory, AzureOpenAIClientFactory>();

        var testConfiguration = new GptChatGrxmlConfiguration();
        testConfiguration.AzureOpenAIEndpoint = "https://test.openai.azure.com/";
        testConfiguration.AzureOpenAIDeploymentName = "test-deployment";
        testConfiguration.AzureOpenAIKey = "test-key";
        services.AddSingleton(Options.Create(testConfiguration));

        services.AddLogging(builder =>
        {
            builder.AddProvider(LogProvider);
        });

        return services;
    }

    protected virtual void Dispose(bool disposing)
    {
        {
            if (disposing)
            {
                LogProvider.Dispose(); // Dispose the LogProvider

                if (ServiceProvider is IDisposable disposable)
                    disposable.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
