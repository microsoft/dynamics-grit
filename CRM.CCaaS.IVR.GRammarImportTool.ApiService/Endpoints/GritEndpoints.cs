using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Endpoints;

internal static class GritEndpoints
{
    public static void MapGritEndpoints(this WebApplication app)
    {
        app.MapGetAntiforgeryTokenEndpoint(); // Map the GET endpoint for antiforgery token

        app.MapGritHubEndpoint(); // Map the SignalR hub endpoint

        // Map the new /grit endpoint
        app.MapGritEndpoint();
    }

    public static void MapGetAntiforgeryTokenEndpoint(this WebApplication app)
    {
        // Endpoint to get anti-forgery token
        app.MapGet("/get-antiforgery-token", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            context.Response.Headers.Append("X-CSRF-TOKEN", tokens.RequestToken);
            return Results.Ok(new { Token = tokens.RequestToken });
        })
        .WithName("GetAntiforgeryToken");
    }

    public static void MapGritHubEndpoint(this WebApplication app)
    {
        // Map the SignalR hub for real-time communication
        app.MapHub<GritHub>("/gritHub");
    }

    public static void MapGritEndpoint(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        // New /grit endpoint: accepts a connectionId and file, starts processing in the background, and notifies client via SignalR
        app.MapPost("/grit", PostGritResponse)
        .DisableAntiforgery()
        .WithName("Grit");
    }

    private static async Task<IResult> PostGritResponse(
        [FromForm] IFormFile file,
        [FromForm] string connectionId,
        [FromServices] IHubContext<GritHub> hubContext,
        [FromServices] GPTPrompter gptPrompter,
        [FromServices] Logger<GritHub> logger,
        HttpContext httpContext)
    {
        if (file.Length == 0)
        {
            return Results.BadRequest("File is empty.");
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
        var cancellationToken = httpContext.RequestAborted;
        _ = Task.Run(async () =>
        {
            try
            {
                // Do NOT dispose memoryStream here; let it be GC'd after task completes
                await gptPrompter.GetFileEntityTypeZipAsync(
                    memoryStream,
                    async (progress, message) =>
                    {
                        await hubContext.Clients.Client(connectionId).SendAsync("Progress", progress, cancellationToken);
                    },
                    async (resultBytes) =>
                    {
                        // Send the zip file as a byte array instead of a base64 string
                        await hubContext.Clients.Client(connectionId).SendAsync("Completed", resultBytes, cancellationToken);
                    }
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while processing the file with connectionId: {ConnectionId}", connectionId);
                await hubContext.Clients.Client(connectionId).SendAsync("Error", ex.Message, cancellationToken);
            }
            finally
            {
                memoryStream.Dispose();
            }
        }, cancellationToken);

        // Immediately return 202 Accepted
        return Results.Accepted();
    }
}
