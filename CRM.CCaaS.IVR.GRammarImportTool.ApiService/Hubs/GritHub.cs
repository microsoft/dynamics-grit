using System.IO;
using System.Text;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using Microsoft.AspNetCore.SignalR;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;

internal class GritHub(ILogger<GptChatGrxmlToMcsConverter> logger,
    [FromKeyedServices(GptChatGrxmlToMcsConverter.SERVICE_KEY)] IGptChat grxmlConverter) : Hub
{
    private readonly IGptChat _grxmlConverter = grxmlConverter;
    private readonly ILogger<GptChatGrxmlToMcsConverter> _logger = logger;

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
    public async Task GrxmlZipConvert(byte[] zipBytes)
    {
        string resultBase64 = string.Empty;
        _logger.LogInformation("GrxmlZipConvert called by {ConnectionId}, bytes: {zipBytes}", Context.ConnectionId, zipBytes?.Length);
        if (zipBytes == null)
        {
            await Clients.Caller.SendAsync("Error", "Uploaded file is null.");
            return;
        }

        try
        {
            using var zipStream = new MemoryStream(zipBytes);

            await _grxmlConverter.ConvertZipAsync(
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

    public async Task GrxmlConvert(byte[] bytes)
    {
        _logger.LogInformation("GrxmlConvert called by {ConnectionId}, bytes: {bytes}", Context.ConnectionId, bytes?.Length);
        if (bytes == null)
        {
            await Clients.Caller.SendAsync("Error", "Uploaded file is null.");
            return;
        }

        try
        {
            var str = Encoding.UTF8.GetString(bytes);

            await _grxmlConverter.ConvertFileAsync(
                str,
                async (progress, message) =>
                {
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultString) =>
                {
                    await Clients.Caller.SendAsync("Completed", resultString);
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
