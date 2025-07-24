using System.IO;
using System.Text;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;

/// <summary>
/// SignalR hub for real-time GRXML and ZIP file conversion.
/// Handles client connections and provides methods for uploading files,
/// reporting progress, and sending conversion results or errors back to the client.
/// </summary>
/// <param name="grxmlConverter">The GRXML converter service.</param>
/// <param name="gptPrompterConfiguration">Configuration options for the GRXML conversion process.</param>
public class GritHub(
    [FromKeyedServices(GptChatGrxmlToMcsConverter.SERVICE_KEY)] IGptChat grxmlConverter,
    IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration) : Hub
{
    private readonly IGptChat _grxmlConverter = grxmlConverter;
    private readonly ILogger<GritHub> _logger = GrITLoggerFactory.CreateLogger<GritHub>();

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
    /// <returns>A task representing the asynchronous operation.</returns>
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

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(gptPrompterConfiguration.Value.MaxAllowedConversionTimeSingleFileSec));
            await Task.Run(() => _grxmlConverter.ConvertZipAsync(
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
            ), cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UploadZipFile");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    /// <summary>
    /// Receives a GRXML file as a byte array from the client, processes it, and sends progress and result events back.
    /// </summary>
    /// <param name="bytes">The GRXML file as a byte array.</param>
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
            _logger.LogError(ex, "Error in UploadZipFile");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }
}
