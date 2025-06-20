using Microsoft.AspNetCore.SignalR;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using System.IO;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;

internal class GritHub(ILogger<GPTPrompter> logger, GPTPrompter gptPrompter) : Hub
{
    private readonly GPTPrompter _gptPrompter = gptPrompter;
    private readonly ILogger<GPTPrompter> _logger = logger;

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.SendAsync("ConnectionId", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Receives a zip file as a byte array from the client, processes it, and sends progress and result events back.
    /// </summary>
    /// <param name="zipBytes">The zip file as a byte array.</param>
    public async Task UploadZipFile(byte[] zipBytes)
    {
        string resultBase64 = string.Empty;
        _logger.LogInformation("UploadZipFile called by {ConnectionId}, bytes: {zipBytes}", Context.ConnectionId, zipBytes?.Length);
        if (zipBytes == null)
        {
            await Clients.Caller.SendAsync("Error", "Uploaded file is null.");
            return;
        }

        try
        {
            using var zipStream = new MemoryStream(zipBytes);

            await _gptPrompter.GetFileEntityTypeZipAsync(
                zipStream,
                async (progress, message) =>
                {
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultBytes) =>
                {
                    resultBase64 = Convert.ToBase64String(resultBytes);
                    await Clients.Caller.SendAsync("Completed", resultBase64);
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Error in UploadZipFile: {Exception}", ex);
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }
}
