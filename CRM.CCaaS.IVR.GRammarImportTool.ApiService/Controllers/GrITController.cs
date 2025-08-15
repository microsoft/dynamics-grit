using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;

/// <summary>
/// Controller for handling ZIP file uploads and initiating background processing.
/// Accepts a ZIP file and SignalR connection ID, processes the file asynchronously,
/// and sends progress and completion events to the client via SignalR.
/// </summary>
[ApiController]
[Route("/grit")]
public class GrITController() : ControllerBase
{
    private readonly ILogger<GrITController> _logger = GrITLoggerFactory.CreateLogger<GrITController>();

    [HttpPost]
    [Route("zip")]
    public async Task PostGritZipResponse(
        [FromForm] IFormFile file,
        [FromKeyedServices(GptChatGrxmlToMcsConverter.SERVICE_KEY)] IGptChat gptGrxmlChat,
        IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration)
    {
        ArgumentNullException.ThrowIfNull(gptGrxmlChat, nameof(gptGrxmlChat));
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration, nameof(gptPrompterConfiguration));
        ArgumentNullException.ThrowIfNull(file, nameof(file));
        ArgumentOutOfRangeException.ThrowIfZero(file.Length, nameof(file.Length));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(file.Length, gptPrompterConfiguration.Value.AllowedUploadFileSizeRangeBytes, nameof(file.Length));

        var hashEnabled = gptPrompterConfiguration.Value.HashFileNameInLogs;
        var fileNameToLog = hashEnabled ? HashHelper.HashSha256Hex(file.FileName) : file.FileName;

        _logger.LogInformation("[PostGritZipResponse] Received ZIP file upload request. HashedFileName={FileName}, Size={FileSizeBytes}",
            fileNameToLog, file.Length);

        var stopwatch = Stopwatch.StartNew();

        // Copy the file to a MemoryStream outside the background task
        var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(gptPrompterConfiguration.Value.MaxAllowedConversionTimeTotalSec));
            var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(gptPrompterConfiguration.Value.ResultStreamChannelCapacity);
            // Do NOT dispose memoryStream here; let it be GC'd after task completes

            var deQueueTask = Task.Run(async () =>
            {
                _logger.LogInformation("[PostGritZipResponse] Starting results dequeue task. ChannelCapacity={ChannelCapacity}", gptPrompterConfiguration.Value.ResultStreamChannelCapacity);
                try
                {
                    SetContentType(Response, "application/x-yaml");
                    await foreach (var partialResult in resultsChannel.Reader.ReadAllAsync(cts.Token))
                    {
                        var keyToLog = hashEnabled ? HashHelper.HashSha256Hex(partialResult.Key) : partialResult.Key;
                        _logger.LogInformation("[PostGritZipResponse] Processed entry. HashedKey={Key}", keyToLog);
                        await Response.WriteAsync($"---\n#{partialResult.Key}\n{SerializeYaml(partialResult.Value)}\n");
                        await Response.Body.FlushAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[PostGritZipResponse] Dequeue task was cancelled");
                }
                _logger.LogInformation("[PostGritZipResponse] Finished results dequeue task");
            }, cts.Token);

            var conversionResult = await gptGrxmlChat.ConvertZipAsync(
                memoryStream,
                resultsChannel
            );

            stopwatch.Stop();

            SetContentType(Response, "text/plain");
            await Response.WriteAsync($"{conversionResult}");
            await Response.Body.FlushAsync();
            _logger.LogInformation("[PostGritZipResponse] File processing completed. Result={Result}, DurationMs={Duration}", conversionResult, stopwatch.ElapsedMilliseconds);

            await deQueueTask;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[PostGritZipResponse] Cancellation exception. HashedFileName={FileName}", fileNameToLog);
            SetContentType(Response, "text/plain");
            await Response.WriteAsync("File processing was cancelled.");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PostGritZipResponse] An error occurred while processing file. HashedFileName={FileName}, Size={FileSizeBytes}", fileNameToLog, file.Length);
            SetContentType(Response, "text/plain");
            await Response.WriteAsync("An error occurred while processing the file.");
            await Response.Body.FlushAsync();
        }
        finally
        {
            memoryStream.Dispose();
        }
    }

    [HttpPost]
    [Route("grxml")]
    public async Task PostGritGrxmlResponse(
        [FromForm] IFormFile file,
        [FromKeyedServices(GptChatGrxmlToMcsConverter.SERVICE_KEY)] IGptChat gptGrxmlChat,
        IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration)
    {
        ArgumentNullException.ThrowIfNull(gptGrxmlChat, nameof(gptGrxmlChat));
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration, nameof(gptPrompterConfiguration));
        ArgumentNullException.ThrowIfNull(file, nameof(file));
        ArgumentOutOfRangeException.ThrowIfZero(file.Length, nameof(file.Length));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(file.Length, gptPrompterConfiguration.Value.AllowedUploadFileSizeRangeBytes, nameof(file.Length));

        var hashEnabled = gptPrompterConfiguration.Value.HashFileNameInLogs;
        var fileNameToLog = hashEnabled ? HashHelper.HashSha256Hex(file.FileName) : file.FileName;

        _logger.LogInformation("[PostGritGrxmlResponse] Received Grxml file upload request. HashedFileName={FileName}, Size={FileSizeBytes}", fileNameToLog, file.Length);

        try
        {
            string grxmlContent;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                grxmlContent = await reader.ReadToEndAsync();
            }

            var converted = await gptGrxmlChat.ConvertFileAsync(grxmlContent);

            if (string.IsNullOrEmpty(converted) || converted.StartsWith("Error:"))
            {
                _logger.LogError("[PostGritGrxmlResponse] Conversion returned empty result. HashedFileName={FileName}, ContentLength={Length}", fileNameToLog, grxmlContent?.Length ?? 0);
                WriteErrorResponse(Response, $"Conversion failed: {converted}", HttpStatusCode.InternalServerError);
                return;
            }
            if (HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation("[PostGritGrxmlResponse] Request was cancelled. HashedFileName={FileName}", fileNameToLog);
                return;
            }
            SetContentType(Response, "application/x-yaml");
            _logger.LogInformation("[PostGritGrxmlResponse] Conversion success. HashedFileName={FileName}, OutputLength={Length}", fileNameToLog, converted?.Length ?? 0);
            Response.StatusCode = StatusCodes.Status200OK;
            await Response.WriteAsync($"---\n#{file.FileName}\n{SerializeYaml(converted)}");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[PostGritGrxmlResponse] Cancellation exception while processing GRXML file. HashedFileName={FileName}", fileNameToLog);
            SetContentType(Response, "text/plain");
            WriteErrorResponse(Response, "File processing was cancelled.", HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PostGritGrxmlResponse] Unexpected error occurred while processing GRXML file. HashedFileName={FileName}", fileNameToLog);
            SetContentType(Response, "text/plain");
            WriteErrorResponse(Response, "Unexpected error occurred while processing the GRXML file.", HttpStatusCode.InternalServerError);
        }
        finally
        {
            await Response.Body.FlushAsync();
        }
    }

    private static void SetContentType(HttpResponse response, string contentType)
    {
        if (string.IsNullOrEmpty(response.ContentType))
        {
            response.ContentType = contentType;
        }
    }

    private static void WriteErrorResponse(HttpResponse response, string message, HttpStatusCode statusCode)
    {
        SetContentType(response, "text/plain");
        response.StatusCode = (int)statusCode;
        response.WriteAsync(message).GetAwaiter().GetResult();
        response.Body.FlushAsync().GetAwaiter().GetResult();
    }

    private static string SerializeYaml(string? data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));
        var serializer = new YamlDotNet.Serialization.Serializer();
        return serializer.Serialize(data);
    }
}
