using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Endpoints;

internal static class GritEndpoints
{
    public static void MapGritEndpoints(this WebApplication app)
    {
        // Map the SignalR hub for real-time communication
        app.MapHub<GritHub>("/gritHub");
    }
}
