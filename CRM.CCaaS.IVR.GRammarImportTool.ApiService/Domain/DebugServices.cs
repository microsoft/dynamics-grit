namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;

internal class DebugServices
{
    private IConfiguration Configuration { get; }
    private readonly ILogger<GPTPrompter> _logger;

    public DebugServices(ILogger<GPTPrompter> logger, IConfiguration configuration)
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
