using Microsoft.AspNetCore.Antiforgery;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities;
using Microsoft.AspNetCore.Mvc;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs; // Add this
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery();
builder.Services.AddSingleton<GPTPrompter>();
builder.Services.AddSignalR();

var app = builder.Build();

app.UseAntiforgery();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

//if (app.Environment.IsDevelopment())
//{
//    app..MapOpenApi();
//}

// Endpoint to get anti-forgery token
app.MapGet("/get-antiforgery-token", (IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Headers.Append("X-CSRF-TOKEN", tokens.RequestToken);
    return Results.Ok(new { Token = tokens.RequestToken });
})
.WithName("GetAntiforgeryToken");

// SignalR endpoint
app.MapHub<GritHub>("/grithub");

// New /grit endpoint: accepts a connectionId and file, starts processing in the background, and notifies client via SignalR
app.MapPost("/grit", async (
    [FromForm] IFormFile file,
    [FromForm] string connectionId,
    [FromServices] IHubContext<GritHub> hubContext,
    HttpContext httpContext
) =>
{
    if (file.Length == 0)
    {
        return Results.BadRequest("File is empty.");
    }

    if (string.IsNullOrWhiteSpace(connectionId))
    {
        return Results.BadRequest("ConnectionId is required.");
    }

    if (string.IsNullOrWhiteSpace(connectionId))
    {
        return Results.BadRequest("ConnectionId is required.");
    }

    // Copy the file to a MemoryStream outside the background task
    var memoryStream = new MemoryStream();
    await file.CopyToAsync(memoryStream);
    memoryStream.Position = 0;

    // Start background processing
    _ = Task.Run(async () =>
    {
        try
        {
            var gptPrompter = app.Services.GetRequiredService<GPTPrompter>();
            // Do NOT dispose memoryStream here; let it be GC'd after task completes
            await gptPrompter.GetFileEntityTypeZipAsync(
                memoryStream,
                async (progress, message) =>
                {
                    await hubContext.Clients.Client(connectionId).SendAsync("Progress", progress);
                },
                async (resultBytes) =>
                {
                    // Send the zip file as a byte array instead of a base64 string
                    await hubContext.Clients.Client(connectionId).SendAsync("Completed", resultBytes);
                }
            );
        }
        catch (Exception ex)
        {
            await hubContext.Clients.Client(connectionId).SendAsync("Error", ex.Message);
        }
        finally
        {
            memoryStream.Dispose();
        }
    });

    // Immediately return 202 Accepted
    return Results.Accepted();
})
.DisableAntiforgery()
.WithName("Grit");

app.MapDefaultEndpoints();

app.Run();
