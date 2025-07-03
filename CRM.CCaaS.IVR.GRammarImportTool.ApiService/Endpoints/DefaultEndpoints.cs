namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Endpoints;

internal static class DefaultEndpoints
{
    public static void MapDefaultEndpoints(this WebApplication app)
    {
        app.MapGet("/default", () => "Default endpoint reached");
        app.MapGet("/health", () => Results.Ok("Healthy"));
    }
}
