using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;

[ApiController]
[Route("[controller]")]
public class AliveController(ILogger<AliveController> logger) : ControllerBase
{
    private readonly ILogger<AliveController> _logger = logger;

    [AllowAnonymous]
    [HttpGet]
    public IActionResult GetLiveness()
    {
        _logger.LogInformation("Liveness check requested at {Time}", DateTime.UtcNow);
        return Ok("Alive");
    }
}
