using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[assembly: InternalsVisibleTo("CRM.CCaaS.IVR.GRammarImportTool.Tests.L1")]
namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController() : ControllerBase
{
    private readonly ILogger<HealthController> _logger = GrITLoggerFactory.CreateLogger<HealthController>();

    [AllowAnonymous]
    [HttpGet]
    public IActionResult GetHealth()
    {
        _logger.LogInformation("Health check requested at {Time}", DateTime.UtcNow);
        return Ok("Healthy");
    }
}
