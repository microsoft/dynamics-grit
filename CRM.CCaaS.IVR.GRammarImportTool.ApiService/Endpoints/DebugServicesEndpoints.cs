using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.DebugServices;
namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Endpoints;

internal static class DebugServicesEndpoints
{
    public static void MapDebugServicesEndpoints(this WebApplication app)
    {
        // Ensure the app is not null
        ArgumentNullException.ThrowIfNull(app);
        // Map the debug services endpoints

        // Map the debug services endpoints
        app.MapGet("/debug/dump-configuration", (ILogger<DebugServices> logger, IConfiguration configuration) =>
        {
            var debugServices = app.Services.GetRequiredService<DebugServices>();
            debugServices.DumpConfiguration();
            return Results.Ok("Configuration dumped to logs.");
        })
        .WithName("DumpConfiguration");
    }
}
