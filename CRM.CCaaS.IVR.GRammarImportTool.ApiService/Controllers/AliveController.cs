// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;

[ApiController]
[Route("[controller]")]
public class AliveController() : ControllerBase
{
    private readonly ILogger<AliveController> _logger = GrITLoggerFactory.CreateLogger<AliveController>();

    [AllowAnonymous]
    [HttpGet]
    public IActionResult GetLiveness()
    {
        _logger.LogInformation("Liveness check requested.");
        return Ok("Alive");
    }
}
