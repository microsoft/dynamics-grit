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
public class GrITHub(
    [FromKeyedServices(GptChatGrxmlToMcsConverter.SERVICE_KEY)] IGptChat grxmlConverter,
    IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration) : Hub
{
    private readonly IGptChat _grxmlConverter = grxmlConverter;
    private readonly ILogger<GrITHub> _logger = GrITLoggerFactory.CreateLogger<GrITHub>();

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
        if (zipBytes == null || zipBytes.Length == 0)
        {
            _logger.LogError("GrxmlZipConvert called with null or empty zipBytes by {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", "GrxmlZipConvert called with null or empty zipBytes");
            return;
        }

        _logger.LogInformation("GrxmlZipConvert called by {ConnectionId}, Zip Size: {zipBytes}", Context.ConnectionId, zipBytes.Length);

        try
        {
            using var zipStream = new MemoryStream(zipBytes);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(gptPrompterConfiguration.Value.MaxAllowedConversionTimeSingleFileSec));
            await Task.Run(() => _grxmlConverter.ConvertZipAsync(
                zipStream,
                async (progress, message) =>
                {
                    _logger.LogInformation("Progress: {Progress}, Message: {Message}", progress, message);
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultBytes) =>
                {
                    _logger.LogInformation("Conversion completed, result size: {ResultSize}", resultBytes.Length);
                    resultBase64 = Convert.ToBase64String(resultBytes);
                    await Clients.Caller.SendAsync("Completed", resultBase64);
                }
            ), cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GrxmlZipConvert");
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
        if (bytes == null || bytes.Length == 0)
        {
            _logger.LogError("GrxmlConvert called with null or empty bytes by {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", "Uploaded file is null or empty");
            return;
        }

        try
        {
            var str = Encoding.UTF8.GetString(bytes);

            await _grxmlConverter.ConvertFileAsync(
                str,
                async (progress, message) =>
                {
                    _logger.LogInformation("Progress: {Progress}, Message: {Message}", progress, message);
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultString) =>
                {
                    _logger.LogInformation("Conversion completed, result size: {ResultSize}", resultString.Length);
                    await Clients.Caller.SendAsync("Completed", resultString);
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GrxmlConvert");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }
}
