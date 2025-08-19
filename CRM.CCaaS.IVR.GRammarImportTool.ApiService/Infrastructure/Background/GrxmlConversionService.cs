using System.ComponentModel;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;

public class GrxmlConversionService : BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly JobTracker _tracker;
    private readonly GptChatGrxmlConfiguration _gptPrompterConfiguration;
    private readonly ILogger<GrxmlConversionService> _logger;

    public GrxmlConversionService(IBackgroundTaskQueue taskQueue, IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration,
         JobTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration);

        _taskQueue = taskQueue;
        _logger = GrITLoggerFactory.CreateLogger<GrxmlConversionService>();
        _gptPrompterConfiguration = gptPrompterConfiguration.Value;
        _tracker = tracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            JobTask? job = await _taskQueue.DequeueAsync(stoppingToken);

            if (job == null)
            {
                _logger.LogInformation("[ExecuteAsync] No job available. Checking cancellation");
                continue;
            }
            if (job.IsReady)
            {
                try
                {
                    _tracker.SetStatus(job.Id, JobStatus.Running);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_gptPrompterConfiguration.MaxAllowedConversionTimeTotalSec));
                    if (job.Worker != null)
                    {
                        await job.Worker(cts.Token);
                        _tracker.SetStatus(job.Id, JobStatus.Completed);
                    }
                    else
                    {
                        _tracker.SetStatus(job.Id, JobStatus.Failed);
                        _logger.LogError("[ExecuteAsync] Job {JobId} failed: Worker is null", job.Id);
                    }
                    _logger.LogInformation("[ExecuteAsync] Job {JobId} processed", job.Id);
                }
                catch (Exception ex)
                {
                    _tracker.SetStatus(job.Id, JobStatus.Failed);
                    _logger.LogError(ex, "[ExecuteAsync] Error processing job {JobId}", job.Id);
                }
            }
            else
            {
                _logger.LogWarning("[ExecuteAsync] Job {JobId} skipped: Job is not ready", job.Id);
            }
        }
        _logger.LogInformation("[ExecuteAsync] GrxmlConversionService is stopping. Cancellation requested");
    }
}
