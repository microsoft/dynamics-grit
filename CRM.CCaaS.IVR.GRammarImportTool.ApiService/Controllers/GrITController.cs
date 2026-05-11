// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Validation;
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
public class GrITController(FileValidator fileValidator) : ControllerBase
{
    private readonly ILogger<GrITController> _logger = GrITLoggerFactory.CreateLogger<GrITController>();
    private readonly FileValidator _fileValidator = fileValidator;

    [HttpPost]
    [Route("zip")]
    public async Task PostGritZipResponse(
        [FromForm] IFormFile file,
        [FromKeyedServices(GptChatGrxmlToMcsConverter.SERVICE_KEY)] IGptChat gptGrxmlChat,
        IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gptGrxmlChat, nameof(gptGrxmlChat));
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration, nameof(gptPrompterConfiguration));
        ArgumentNullException.ThrowIfNull(file, nameof(file));
        ArgumentOutOfRangeException.ThrowIfZero(file.Length, nameof(file.Length));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(file.Length, gptPrompterConfiguration.Value.AllowedUploadFileSizeRangeBytes, nameof(file.Length));

        var hashEnabled = gptPrompterConfiguration.Value.HashFileNameInLogs;
        var fileNameToLog = hashEnabled ? HashHelper.HashSha256Hex(file.FileName) : file.FileName;
        var fileNameLogLabel = hashEnabled ? "HashedFileName" : "FileName";

        var validationResult = await _fileValidator.ValidateUploadedFileAsync(file);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("[PostGritZipResponse] File validation failed. {fileNameLogLabel}={FileName}, Reason={Reason}",
                fileNameLogLabel, fileNameToLog, validationResult.ErrorMessage);

            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.ContentType = "text/plain";
            var errorMessage = validationResult.ErrorMessage ?? "Error validating input";
            await Response.WriteAsync(errorMessage, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        _logger.LogInformation("[PostGritZipResponse] Received ZIP file upload request. {fileNameLogLabel}={FileName}, Size={FileSizeBytes}",
            fileNameLogLabel, fileNameToLog, file.Length);

        var stopwatch = Stopwatch.StartNew();

        // Copy the file to a MemoryStream outside the background task
        var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(gptPrompterConfiguration.Value.MaxAllowedConversionTimeTotalSec));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(gptPrompterConfiguration.Value.ResultStreamChannelCapacity);
            // Do NOT dispose memoryStream here; let it be GC'd after task completes

            var deQueueTask = Task.Run(async () =>
            {
                _logger.LogInformation("[PostGritZipResponse] Starting results dequeue task. ChannelCapacity={ChannelCapacity}", gptPrompterConfiguration.Value.ResultStreamChannelCapacity);
                try
                {
                    SetContentType(Response, "application/x-yaml");
                    await foreach (var partialResult in resultsChannel.Reader.ReadAllAsync(linkedCts.Token))
                    {
                        var keyToLog = hashEnabled ? HashHelper.HashSha256Hex(partialResult.Key) : partialResult.Key;
                        _logger.LogInformation("[PostGritZipResponse] Processed entry. HashedKey={Key}", keyToLog);
                        await Response.WriteAsync($"---\n#{partialResult.Key}\n{YamlHelper.SerializeYaml(partialResult.Value)}\n", linkedCts.Token);
                        await Response.Body.FlushAsync(linkedCts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[PostGritZipResponse] Dequeue task was cancelled");
                }
                _logger.LogInformation("[PostGritZipResponse] Finished results dequeue task");
            }, linkedCts.Token);

            var conversionResult = await gptGrxmlChat.ConvertZipAsync(
                memoryStream,
                resultsChannel
            );

            stopwatch.Stop();

            SetContentType(Response, "text/plain");
            await Response.WriteAsync($"{conversionResult}", linkedCts.Token);
            await Response.Body.FlushAsync(linkedCts.Token);
            _logger.LogInformation("[PostGritZipResponse] File processing completed. Result={Result}, DurationMs={Duration}", conversionResult, stopwatch.ElapsedMilliseconds);

            await deQueueTask;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[PostGritZipResponse] Cancellation exception. HashedFileName={FileName}", fileNameToLog);
            SetContentType(Response, "text/plain");
            await Response.WriteAsync("File processing was cancelled.", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PostGritZipResponse] An error occurred while processing file. HashedFileName={FileName}, Size={FileSizeBytes}", fileNameToLog, file.Length);
            SetContentType(Response, "text/plain");
            await Response.WriteAsync("An error occurred while processing the file.", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            await Response.Body.FlushAsync(cancellationToken);
            memoryStream.Dispose();
        }
    }

    [HttpPost]
    [Route("grxml")]
    public async Task PostGritGrxmlResponse(
        [FromForm] IFormFile file,
        [FromKeyedServices(GptChatGrxmlToMcsConverter.SERVICE_KEY)] IGptChat gptGrxmlChat,
        IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gptGrxmlChat, nameof(gptGrxmlChat));
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration, nameof(gptPrompterConfiguration));
        ArgumentNullException.ThrowIfNull(file, nameof(file));
        ArgumentOutOfRangeException.ThrowIfZero(file.Length, nameof(file.Length));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(file.Length, gptPrompterConfiguration.Value.AllowedUploadFileSizeRangeBytes, nameof(file.Length));

        var hashEnabled = gptPrompterConfiguration.Value.HashFileNameInLogs;
        var fileNameToLog = hashEnabled ? HashHelper.HashSha256Hex(file.FileName) : file.FileName;
        var logFileNameLabel = hashEnabled ? "HashedFileName" : "FileName";

        _logger.LogInformation("[PostGritGrxmlResponse] Received Grxml file upload request. {logFileNameLabel}={FileName}, Size={FileSizeBytes}", logFileNameLabel, fileNameToLog, file.Length);

        var validationResult = await _fileValidator.ValidateUploadedFileAsync(file, true);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("[PostGritGrxmlResponse] File validation failed. {logFileNameLabel}={FileName}, Reason={Reason}",
                logFileNameLabel, fileNameToLog, validationResult.ErrorMessage);
            var errorMessage = validationResult.ErrorMessage ?? "Error validating input";
            await WriteErrorResponse(Response, errorMessage, HttpStatusCode.BadRequest, cancellationToken);
            return;
        }

        try
        {
            string grxmlContent;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                grxmlContent = await reader.ReadToEndAsync(cancellationToken);
            }

            var converted = await gptGrxmlChat.ConvertFileAsync(grxmlContent);

            if (string.IsNullOrEmpty(converted) || converted.StartsWith("Error:"))
            {
                _logger.LogError("[PostGritGrxmlResponse] Conversion returned empty result. HashedFileName={FileName}, ContentLength={Length}", fileNameToLog, grxmlContent?.Length ?? 0);
                await WriteErrorResponse(Response, $"Conversion failed: {converted}", HttpStatusCode.InternalServerError, cancellationToken);
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
            await Response.WriteAsync($"---\n#{file.FileName}\n{YamlHelper.SerializeYaml(converted)}", cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[PostGritGrxmlResponse] Cancellation exception while processing GRXML file. HashedFileName={FileName}", fileNameToLog);
            SetContentType(Response, "text/plain");
            await WriteErrorResponse(Response, "File processing was cancelled.", HttpStatusCode.RequestTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PostGritGrxmlResponse] Unexpected error occurred while processing GRXML file. HashedFileName={FileName}", fileNameToLog);
            SetContentType(Response, "text/plain");
            await WriteErrorResponse(Response, "Unexpected error occurred while processing the GRXML file.", HttpStatusCode.InternalServerError, cancellationToken);
        }
        finally
        {
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static void SetContentType(HttpResponse response, string contentType)
    {
        if (string.IsNullOrEmpty(response.ContentType))
        {
            response.ContentType = contentType;
        }
    }

    private static async Task WriteErrorResponse(HttpResponse response, string message, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        SetContentType(response, "text/plain");
        response.StatusCode = (int)statusCode;
        await response.WriteAsync(message, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
