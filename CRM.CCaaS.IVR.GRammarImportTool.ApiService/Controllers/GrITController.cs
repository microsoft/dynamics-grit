using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
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

        _logger.LogInformation("Received ZIP file upload request");

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
                _logger.LogInformation("Starting results dequeue task");
                try
                {
                    _logger.LogInformation("Starting results dequeue");
                    SetContentType(Response, "application/x-yaml");
                    await foreach (var partialResult in resultsChannel.Reader.ReadAllAsync(cts.Token))
                    {
                        _logger.LogInformation("Processed entry: {Key}", partialResult.Key);
                        await Response.WriteAsync($"---\n#{partialResult.Key}\n{SerializeYaml(partialResult.Value)}\n");
                        await Response.Body.FlushAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Dequeue task was cancelled");
                }
                _logger.LogInformation("Finished results dequeue task");
            }, cts.Token);

            var conversionResult = await gptGrxmlChat.ConvertZipAsync(
                memoryStream,
                resultsChannel
            );
            SetContentType(Response, "text/plain");
            await Response.WriteAsync($"{conversionResult}");
            await Response.Body.FlushAsync();
            _logger.LogInformation("File processing completed with result: {Result}", conversionResult);

            await deQueueTask;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Cancellation exception");
            SetContentType(Response, "text/plain");
            await Response.WriteAsync("File processing was cancelled.");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing file");
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

        _logger.LogInformation("Received Grxml file upload request");

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
                _logger.LogError("Conversion returned empty result for file: {FileName}", file.FileName);
                WriteErrorResponse(Response, $"Conversion failed: {converted}", HttpStatusCode.InternalServerError);
                return;
            }
            if (HttpContext.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation("Request was cancelled, stopping processing for file: {FileName}", file.FileName);
                return;
            }
            SetContentType(Response, "application/x-yaml");
            _logger.LogInformation("Conversion success for file: {FileName}", file.FileName);
            Response.StatusCode = StatusCodes.Status200OK;
            await Response.WriteAsync($"---\n#{file.FileName}\n{SerializeYaml(converted)}");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Cancellation exception while processing GRXML file.");
            SetContentType(Response, "text/plain");
            WriteErrorResponse(Response, "File processing was cancelled.", HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the GRXML file.");
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

    private static string SerializeYaml(string data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));
        var serializer = new YamlDotNet.Serialization.Serializer();
        return serializer.Serialize(data);
    }
}
