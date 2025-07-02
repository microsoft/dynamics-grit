using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.DebugServices;

internal class DebugServices
{
    private IConfiguration Configuration { get; }
    private readonly ILogger<GptChatGrxmlToMcsConverter> _logger;

    public DebugServices(ILogger<GptChatGrxmlToMcsConverter> logger, IConfiguration configuration)
    {
        Configuration = configuration;
        _logger = logger;
    }

    public void DumpConfiguration()
    {
        ArgumentNullException.ThrowIfNull(Configuration);

        var debugView = ((IConfigurationRoot)Configuration).GetDebugView();
        _logger.LogInformation("Debug View: {DebugView}", debugView);
    }
}
