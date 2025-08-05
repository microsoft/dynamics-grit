using System.Diagnostics;
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
        var timestamp = DateTime.UtcNow;
        _logger.LogInformation("Client connected. ConnectionId={ConnectionId}, Timestamp={Timestamp}", Context.ConnectionId, timestamp);
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
            _logger.LogError("GrxmlZipConvert called with null or empty zipBytes. ConnectionId={ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", "GrxmlZipConvert called with null or empty zipBytes");
            return;
        }

        _logger.LogInformation("GrxmlZipConvert called. ConnectionId={ConnectionId}, ZipSizeBytes={ZipSizeBytes}, Timestamp={Timestamp}",
            Context.ConnectionId, zipBytes.Length, DateTime.UtcNow);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var zipStream = new MemoryStream(zipBytes);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(gptPrompterConfiguration.Value.MaxAllowedConversionTimeSingleFileSec));
            await Task.Run(() => _grxmlConverter.ConvertZipAsync(
                zipStream,
                async (progress, message) =>
                {
                    _logger.LogInformation("Progress update. ConnectionId={ConnectionId}, Progress={Progress}, Message={Message}, Timestamp={Timestamp}",
                        Context.ConnectionId, progress, message, DateTime.UtcNow);
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultBytes) =>
                {
                    stopwatch.Stop();
                    _logger.LogInformation("Conversion completed. ConnectionId={ConnectionId}, ResultSizeBytes={ResultSizeBytes}, DurationMs={Duration}, Timestamp={Timestamp}",
                        Context.ConnectionId, resultBytes.Length, stopwatch.ElapsedMilliseconds, DateTime.UtcNow);
                    resultBase64 = Convert.ToBase64String(resultBytes);
                    await Clients.Caller.SendAsync("Completed", resultBase64);
                }
            ), cts.Token);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in GrxmlZipConvert. ConnectionId={ConnectionId}, DurationMs={Duration}, Timestamp={Timestamp}",
                Context.ConnectionId, stopwatch.ElapsedMilliseconds, DateTime.UtcNow);
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    /// <summary>
    /// Receives a GRXML file as a byte array from the client, processes it, and sends progress and result events back.
    /// </summary>
    /// <param name="bytes">The GRXML file as a byte array.</param>
    public async Task GrxmlConvert(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            _logger.LogError("GrxmlConvert called with null or empty bytes. ConnectionId={ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", "Uploaded file is null or empty");
            return;
        }

        _logger.LogInformation("GrxmlConvert called. ConnectionId={ConnectionId}, InputSizeBytes={InputSizeBytes}, Timestamp={Timestamp}",
            Context.ConnectionId, bytes.Length, DateTime.UtcNow);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var str = Encoding.UTF8.GetString(bytes);

            await _grxmlConverter.ConvertFileAsync(
                str,
                async (progress, message) =>
                {
                    _logger.LogInformation("Progress update. ConnectionId={ConnectionId}, Progress={Progress}, Message={Message}, Timestamp={Timestamp}",
                        Context.ConnectionId, progress, message, DateTime.UtcNow);
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultString) =>
                {
                    stopwatch.Stop();
                    _logger.LogInformation("Conversion completed. ConnectionId={ConnectionId}, ResultSizeChars={ResultSizeChars}, DurationMs={Duration}, Timestamp={Timestamp}",
                        Context.ConnectionId, resultString.Length, stopwatch.ElapsedMilliseconds, DateTime.UtcNow);
                    await Clients.Caller.SendAsync("Completed", resultString);
                }
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in GrxmlConvert. ConnectionId={ConnectionId}, DurationMs={Duration}, Timestamp={Timestamp}",
                Context.ConnectionId, stopwatch.ElapsedMilliseconds, DateTime.UtcNow);
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }
}
