using System.Diagnostics;
using System.IO;
using System.Text;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
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
    private static readonly char[] Separators = ['|'];

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("[OnConnectedAsync] Client connected. ConnectionId={ConnectionId}", HashHelper.HashSha256Hex(Context.ConnectionId));
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
        var hashedConnectionId = HashHelper.HashSha256Hex(Context.ConnectionId);
        string resultBase64 = string.Empty;
        if (zipBytes == null || zipBytes.Length == 0)
        {
            _logger.LogError("[GrxmlZipConvert] Called with null or empty zipBytes. ConnectionId={ConnectionId}", hashedConnectionId);
            await Clients.Caller.SendAsync("Error", "GrxmlZipConvert called with null or empty zipBytes");
            return;
        }

        _logger.LogInformation("[GrxmlZipConvert] Start. ConnectionId={ConnectionId}, ZipSizeBytes={ZipSizeBytes}",
            hashedConnectionId, zipBytes.Length);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var zipStream = new MemoryStream(zipBytes);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(gptPrompterConfiguration.Value.MaxAllowedConversionTimeSingleFileSec));
            await Task.Run(() => _grxmlConverter.ConvertZipAsync(
                zipStream,
                async (progress, message) =>
                {
                    _logger.LogInformation("[GrxmlZipConvert] Progress update. ConnectionId={ConnectionId}, Progress={Progress}, Message={Message}",
                        hashedConnectionId, progress, MaskFileNameInMessage(message));
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultBytes) =>
                {
                    stopwatch.Stop();
                    _logger.LogInformation("[GrxmlZipConvert] Conversion completed. ConnectionId={ConnectionId}, ResultSizeBytes={ResultSizeBytes}, DurationMs={Duration}",
                        hashedConnectionId, resultBytes.Length, stopwatch.ElapsedMilliseconds);
                    resultBase64 = Convert.ToBase64String(resultBytes);
                    await Clients.Caller.SendAsync("Completed", resultBase64);
                }
            ), cts.Token);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[GrxmlZipConvert] Error in GrxmlZipConvert. ConnectionId={ConnectionId}, DurationMs={Duration}",
                hashedConnectionId, stopwatch.ElapsedMilliseconds);
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    /// <summary>
    /// Receives a GRXML file as a byte array from the client, processes it, and sends progress and result events back.
    /// </summary>
    /// <param name="bytes">The GRXML file as a byte array.</param>
    public async Task GrxmlConvert(byte[] bytes)
    {
        var hashedConnectionId = HashHelper.HashSha256Hex(Context.ConnectionId);
        if (bytes == null || bytes.Length == 0)
        {
            _logger.LogError("[GrxmlConvert] Called with null or empty bytes. ConnectionId={ConnectionId}", hashedConnectionId);
            await Clients.Caller.SendAsync("Error", "Uploaded file is null or empty");
            return;
        }

        _logger.LogInformation("[GrxmlConvert] Start. ConnectionId={ConnectionId}, InputSizeBytes={InputSizeBytes}",
            hashedConnectionId, bytes.Length);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var str = Encoding.UTF8.GetString(bytes);

            await _grxmlConverter.ConvertFileAsync(
                str,
                async (progress, message) =>
                {
                    _logger.LogInformation("[GrxmlConvert] Progress update. ConnectionId={ConnectionId}, Progress={Progress}, Message={Message}",
                        hashedConnectionId, progress, MaskFileNameInMessage(message));
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultString) =>
                {
                    stopwatch.Stop();
                    _logger.LogInformation("[GrxmlConvert] Conversion completed. ConnectionId={ConnectionId}, ResultSizeChars={ResultSizeChars}, DurationMs={Duration}",
                        hashedConnectionId, resultString.Length, stopwatch.ElapsedMilliseconds);
                    await Clients.Caller.SendAsync("Completed", resultString);
                }
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in GrxmlConvert. ConnectionId={ConnectionId}, DurationMs={Duration}",
                hashedConnectionId, stopwatch.ElapsedMilliseconds);
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    private string MaskFileNameInMessage(string message)
    {
        var fileName = message.Split(Separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(fileName))
            return message;

        if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
            return message;

        return message.Replace(fileName, HashHelper.HashSha256Hex(fileName), StringComparison.CurrentCulture);
    }
}
