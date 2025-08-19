using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
[ApiController]
[Route("/grit-async/tasks")]
public class GrITControllerAsync : ControllerBase
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly JobTracker _tracker;
    private readonly ILogger<GrITControllerAsync> _logger = GrITLoggerFactory.CreateLogger<GrITControllerAsync>();
    private readonly IServiceProvider _serviceProvider;

    public GrITControllerAsync(IBackgroundTaskQueue queue, JobTracker tracker, IServiceProvider serviceProvider)
    {
        _queue = queue;
        _tracker = tracker;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    [Route("zip")]
    public async Task<IActionResult> AddZipTask(
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

        var hashedZipFileName = HashHelper.HashSha256Hex(file.FileName);

        _logger.LogInformation("[AddZipTask] Received ZIP file upload request. HashedFileName={FileName}, Size={FileSizeBytes}",
            hashedZipFileName, file.Length);

        var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        try
        {
            var zipConvertTask = new JobTask(memoryStream, JobType.ConvertZipGrxml, null);
            if (await _queue.EnqueueAsync(zipConvertTask, cancellationToken))
            {
                _tracker.SetStatus(zipConvertTask.Id, JobStatus.Pending);
                _logger.LogInformation("[AddZipTask] Job {JobId} enqueued successfully.", zipConvertTask.Id);
                return Accepted(new { JobId = zipConvertTask.Id });
            }
            else
            {
                _logger.LogError("[AddZipTask] Failed to enqueue job {JobId}.", zipConvertTask.Id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Failed to enqueue job");
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[AddZipTask] Request was cancelled while processing ZIP file. HashedFileName={FileName}", hashedZipFileName);
            return StatusCode(StatusCodes.Status408RequestTimeout, "Request was cancelled");
        }
        finally
        {
            memoryStream.Dispose();
        }
    }

    [HttpPost]
    [Route("grxml")]
    public async Task<IActionResult> AddGrxmlTask(
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

        var hashedGrxmlFileName = HashHelper.HashSha256Hex(file.FileName);

        _logger.LogInformation("[AddGrxmlTask] Received GRXML file upload request. HashedFileName={FileName}, Size={FileSizeBytes}",
            hashedGrxmlFileName, file.Length);

        try
        {
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            var grxmlConvertTask = new JobTask(memoryStream, JobType.ConvertSingleGrxml, null);
            if (await _queue.EnqueueAsync(grxmlConvertTask, cancellationToken))
            {
                _tracker.SetStatus(grxmlConvertTask.Id, JobStatus.Pending);
                _logger.LogInformation("[AddGrxmlTask] Job {JobId} enqueued successfully.", grxmlConvertTask.Id);
                return Accepted(new { JobId = grxmlConvertTask.Id });
            }
            else
            {
                _logger.LogError("[AddGrxmlTask] Failed to enqueue job {JobId}.", grxmlConvertTask.Id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Failed to enqueue job");
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[AddGrxmlTask] Request was cancelled while processing GRXML file. HashedFileName={FileName}", hashedGrxmlFileName);
            return StatusCode(StatusCodes.Status408RequestTimeout, "Request was cancelled");
        }
    }

    [HttpGet]
    [Route("status/{jobId}")]
    public IActionResult GetStatus(string jobId)
    {
        _logger.LogInformation("[GetStatus] Checking status for job {JobId}", jobId);
        var status = _tracker.GetStatus(jobId);
        if (status == null)
        {
            _logger.LogWarning("[GetStatus] Job {JobId} not found.", jobId);
            return NotFound();
        }

        return Ok(new { JobId = jobId, Status = status.ToString() });
    }

    [HttpGet]
    [Route("results/{jobId}/{format?}")]
    public async Task<IActionResult> GetResultsAsync(string jobId, string format = "json")
    {
        _logger.LogInformation("[GetResultsAsync] Retrieving results for job {JobId} with format {Format}", jobId, format);
        var status = _tracker.GetStatus(jobId);
        if (status == null)
        {
            _logger.LogWarning("[GetResultsAsync] Job {JobId} not found.", jobId);
            return NotFound();
        }

        if (status == JobStatus.Completed)
        {
            var store = _serviceProvider.GetRequiredKeyedService<IConversionResultsStore>(InMemoryConversionResultsStore.SERVICE_KEY);
            var results = await store.GetResultAsync(jobId);

            if (results == null)
            {
                _logger.LogWarning("[GetResultsAsync] No results found for job {JobId}. Returning null.", jobId);
                return Ok(new { JobId = jobId, Results = "null" });
            }

            _logger.LogInformation("[GetResultsAsync] Job {JobId} completed successfully. Results count: {Count}", jobId, results.ResultData.Count);
            switch (format?.ToLower())
            {
                case "json":
                    return Ok(new { JobId = jobId, Results = results });
                case "pretty":
                    var stringBuilder = new System.Text.StringBuilder();
                    foreach (var result in results.ResultData)
                    {
                        stringBuilder.AppendLine($"# {result.Key}");
                        stringBuilder.AppendLine("---");
                        stringBuilder.AppendLine(YamlHelper.SerializeYaml(result.Value));
                    }
                    return Ok(stringBuilder.ToString());
                default:
                    return Ok(new { JobId = jobId, Results = results });
            }
        }

        _logger.LogInformation("[GetResultsAsync] Job {JobId} is not completed yet. Current status: {Status}", jobId, status);
        return Ok(new { JobId = jobId, Status = status.ToString() });
    }
}
