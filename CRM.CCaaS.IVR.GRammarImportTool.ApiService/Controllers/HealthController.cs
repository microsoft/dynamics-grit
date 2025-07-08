using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[assembly: InternalsVisibleTo("CRM.CCaaS.IVR.GRammarImportTool.Tests.L1")]
namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController(ILogger<HealthController> logger) : ControllerBase
{
    private readonly ILogger<HealthController> _logger = logger;

    [AllowAnonymous]
    [HttpGet]
    public IActionResult GetHealth()
    {
        _logger.LogInformation("Health check requested at {Time}", DateTime.UtcNow);
        return Ok("Healthy");
    }
}
