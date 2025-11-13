// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Diagnostics.CodeAnalysis;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Endpoints;

[ExcludeFromCodeCoverage]
internal static class GrITEndpoints
{
    public static void MapGrITEndpoints(this WebApplication app)
    {
        // Map the SignalR hub for real-time communication
        app.MapHub<GrITHub>("/gritHub");
    }
}
