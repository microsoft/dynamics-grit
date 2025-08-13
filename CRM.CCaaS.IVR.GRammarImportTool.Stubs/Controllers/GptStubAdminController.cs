using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Controllers;

[ApiController]
[Route("api/stub")]
public sealed class GptStubAdminController(
    StubBehaviorState state,
    IOptionsMonitor<StubBehaviorOptions> defaults) : ControllerBase
{
    private const string AdminHeader = "X-Stub-Admin-Key";
    private readonly StubBehaviorState _state = state;
    private readonly IOptionsMonitor<StubBehaviorOptions> _defaults = defaults;

    [HttpGet("behavior")]
    [ProducesResponseType(typeof(StubBehaviorOptions), StatusCodes.Status200OK)]
    public ActionResult<StubBehaviorOptions> GetBehavior()
    {
        var snapshot = _state.Snapshot();
        snapshot.AdminApiKey = "********";
        return Ok(snapshot);
    }

    [HttpPost("behavior")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult SetBehavior([FromBody, Required] StubBehaviorOptions model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!TryValidateAdmin(out var configuredKey, out var failure)) return failure!;
        model.AdminApiKey = configuredKey;

        _state.Replace(model);
        return NoContent();
    }

    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult ResetToDefaults()
    {
        if (!TryValidateAdmin(out _, out var failure)) return failure!;
        _state.Replace(_defaults.CurrentValue);
        return NoContent();
    }

    // Quick mode routes

    [HttpPost("mode/error")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult SetErrorMode([FromQuery] int status = 500, [FromQuery] string? message = null)
        => Update(o => { o.Mode = StubMode.ErrorResponse; o.ErrorStatusCode = status; o.ErrorMessage = message ?? o.ErrorMessage; });

    [HttpPost("mode/refuse")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult SetRefuseConnection()
        => Update(o => o.Mode = StubMode.RefuseConnection);

    [HttpPost("mode/bad/all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult SetBadAll([FromQuery] bool malformed = false)
        => Update(o => { o.Mode = StubMode.BadResultsAll; o.ProduceMalformedJson = malformed; });

    [HttpPost("mode/bad/some")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult SetBadSome([FromQuery] int percentage = 25, [FromQuery] int maxPerArray = 3)
        => Update(o => { o.Mode = StubMode.BadResultsSome; o.BadItemsPercentage = percentage; o.MaxCorruptionsPerArray = maxPerArray; });

    private IActionResult Update(Action<StubBehaviorOptions> mutator)
    {
        if (!TryValidateAdmin(out _, out var failure)) return failure!;
        _state.Update(mutator);
        return NoContent();
    }

    private bool TryValidateAdmin(out string configuredKey, out IActionResult? failure)
    {
        configuredKey = _defaults.CurrentValue.AdminApiKey;
        failure = null;

        if (!Request.Headers.TryGetValue(AdminHeader, out var provided) || string.IsNullOrWhiteSpace(provided))
        {
            failure = Unauthorized(new { error = "Missing admin key header." });
            return false;
        }

        if (!string.Equals(configuredKey, provided.ToString(), StringComparison.Ordinal))
        {
            failure = Unauthorized(new { error = "Invalid admin key." });
            return false;
        }

        return true;
    }
}