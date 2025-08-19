using System.Collections.Concurrent;
using System.Threading.Channels;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Controllers;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<JobTask> _queue;
    private readonly ILogger<BackgroundTaskQueue> _logger;
    private readonly GptChatGrxmlConfiguration _gptPrompterConfiguration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConversionResultsStore _conversionResultsStore;
    private readonly IGptChat _gptChat;

    public BackgroundTaskQueue(IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration, nameof(gptPrompterConfiguration));

        _logger = GrITLoggerFactory.CreateLogger<BackgroundTaskQueue>();
        _gptPrompterConfiguration = gptPrompterConfiguration.Value;
        _serviceProvider = serviceProvider;
        _conversionResultsStore = _serviceProvider.GetRequiredKeyedService<IConversionResultsStore>(InMemoryConversionResultsStore.SERVICE_KEY);
        _gptChat = _serviceProvider.GetRequiredKeyedService<IGptChat>(GptChatGrxmlToMcsConverter.SERVICE_KEY);

        _logger.LogInformation("[BackgroundTaskQueue] Initializing with capacity {Capacity}.", _gptPrompterConfiguration.BackgroundTasksQueueCapacity);
        var options = new BoundedChannelOptions(_gptPrompterConfiguration.BackgroundTasksQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<JobTask>(options);
    }

    public async Task<bool> EnqueueAsync(JobTask jobTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobTask);
        _logger.LogInformation("[EnqueueAsync] Adding job {JobId} to the queue.", jobTask.Id);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_gptPrompterConfiguration.AddBackgroundTaskMaxWaitTimeSec));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            await _queue.Writer.WriteAsync(jobTask, linkedCts.Token);
        }
        catch (ChannelClosedException ex)
        {
            _logger.LogError(ex, "[EnqueueAsync] Failed to enqueue job {JobId}: Channel is closed.", jobTask.Id);
            return false;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "[EnqueueAsync] Timeout enqueuing job {JobId}.", jobTask.Id);
            if (_queue.Reader.Count >= _gptPrompterConfiguration.BackgroundTasksQueueCapacity)
            {
                _logger.LogWarning("[EnqueueAsync] Queue is full. Job {JobId} could not be enqueued.", jobTask.Id);
            }
            return false;
        }
        return true;
    }

    public async Task<JobTask?> DequeueAsync(CancellationToken cancellationToken)
    {
        try
        {
            var job = await _queue.Reader.ReadAsync(cancellationToken);
            _logger.LogInformation("[DequeueAsync] Job {JobId} dequeued from the queue.", job.Id);
            switch (job.Type)
            {
                case JobType.ConvertZipGrxml:
                    _logger.LogInformation("[DequeueAsync] Processing ZIP file job {JobId}.", job.Id);
                    job.Worker = async (ct) =>
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_gptPrompterConfiguration.MaxAllowedConversionTimeTotalSec));
                        var resultsChannel = Channel.CreateBounded<KeyValuePair<string, string>>(_gptPrompterConfiguration.ResultStreamChannelCapacity);

                        var dequeTask = Task.Run(async () =>
                        {
                            _logger.LogInformation("[DequeueAsync] Starting results dequeue task for job {JobId}.", job.Id);
                            await ConvertZipGrxmlResultHandler(job.Id, resultsChannel, cts.Token);
                        }, cts.Token);

                        await _gptChat.ConvertZipAsync(new MemoryStream(job.Payload), resultsChannel);

                        await dequeTask;
                        _logger.LogInformation("[DequeueAsync] ZIP file job {JobId} completed.", job.Id);
                    };
                    job.IsReady = true;
                    break;
                case JobType.ConvertSingleGrxml:
                    _logger.LogInformation("[DequeueAsync] Processing GRXML conversion job {JobId}.", job.Id);
                    job.Worker = async (ct) =>
                    {
                        var converted = await _gptChat.ConvertFileAsync(System.Text.Encoding.UTF8.GetString(job.Payload));

                        var conversationResult = new ConversionResult(new ConcurrentDictionary<string, string>());
                        conversationResult.ResultData["converted.yaml"] = converted;
                        await _conversionResultsStore.AddResultAsync(job.Id, conversationResult);

                        _logger.LogInformation("[DequeueAsync] GRXML conversion job {JobId} completed.", job.Id);
                    };
                    job.IsReady = true;
                    break;
                default:
                    _logger.LogWarning("[DequeueAsync] Unknown job type {JobType} for job {JobId}.", job.Type, job.Id);
                    break;
            }
            return job;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[DequeueAsync] Dequeue operation was canceled.");
            return null;
        }
    }

    private async Task ConvertZipGrxmlResultHandler(string jobID, Channel<KeyValuePair<string, string>> results, CancellationToken ct)
    {
        _logger.LogInformation("[ConvertZipGrxmlResultHandler] Starting to process results from the channel.");
        var conversationResult = new ConversionResult(new ConcurrentDictionary<string, string>());
        try
        {
            await foreach (var result in results.Reader.ReadAllAsync(ct))
            {
                var hashedFileName = HashHelper.HashSha256Hex(result.Key);
                _logger.LogInformation("[ConvertZipGrxmlResultHandler] Processing result for file {FileName}.", hashedFileName);
                conversationResult.ResultData[result.Key] = result.Value;
            }
            _logger.LogInformation("[ConvertZipGrxmlResultHandler] Completed processing all results from the channel.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[ConvertZipGrxmlResultHandler] Processing was canceled.");
        }

        await _conversionResultsStore.AddResultAsync(jobID, conversationResult);
    }
}
