using System.Reflection;
using System.Runtime;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.DebugServices;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Endpoints;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("AppSettings.json", optional: false, reloadOnChange: true)
    .AddUserSecrets(Assembly.GetExecutingAssembly())
    .AddJsonFile($"AppSettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "GPTPrompter")
    .AddCommandLine(args);

builder.Logging.AddConsole()
    .AddConfiguration(builder.Configuration.GetSection("Logging"));

// Add services to the container.
builder.Services.Configure<GptChatConfiguration>(builder.Configuration.GetSection(GptChatConfiguration.SectionName));

builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery();
builder.Services.AddKeyedTransient<IGptChat, GptChatGrxmlToMcsConverter>(GptChatGrxmlToMcsConverter.SERVICE_KEY);
builder.Services.AddTransient<DebugServices>();

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
})
.AddHubOptions<GritHub>(hubOptions =>
{
    hubOptions.MaximumReceiveMessageSize = 1 * 1024 * 1024; // 1 MB
});

// Add CORS policy for SignalR/WebSockets
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true) // Allow all origins for testing; restrict in production
            .AllowCredentials();
    });
});

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Main");

app.UseCors(); // Enable CORS

app.UseAntiforgery();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

//if (app.Environment.IsDevelopment())
//{
//    app..MapOpenApi();
//}
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.MapDebugServicesEndpoints();
}

app.MapGritEndpoints();

app.MapDefaultEndpoints();

logger.LogInformation("Application started at {Timestamp}.", DateTime.UtcNow);

app.Run();
