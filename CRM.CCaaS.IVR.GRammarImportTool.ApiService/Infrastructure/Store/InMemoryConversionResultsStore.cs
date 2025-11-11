using System.Collections.Concurrent;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;

public sealed class InMemoryConversionResultsStore : IConversionResultsStore
{
    public const string SERVICE_KEY = "InMemoryConversionResultsStore";
    // jobId -> ConversationResult
    private readonly ConcurrentDictionary<string, ConversionResult> _store = new();
    private readonly Func<ConversionResult, ConversionResult, ConversionResult> _appendMerger;
    private readonly ILogger<InMemoryConversionResultsStore> _logger;
    private readonly IOptions<GptChatGrxmlConfiguration> _gptPrompterConfiguration;
    private readonly JobTracker _jobTracker;

    /// <summary>
    /// Initializes a new instance of the in-memory results store.
    /// </summary>
    /// <param name="gptPrompterConfiguration">Application configuration providing capacity limits.</param>
    /// <param name="jobTracker">Job tracker used to keep job statuses in sync when items are evicted.</param>
    /// <param name="appendMerger">
    /// Optional merge function used by <see cref="AppendResultAsync(string, ConversionResult)"/>.
    /// Signature: merge(existing, added) => mergedExisting. Defaults to line-append per file key.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="gptPrompterConfiguration"/> is null.</exception>
    public InMemoryConversionResultsStore(
        IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration,
        JobTracker jobTracker,
        Func<ConversionResult, ConversionResult, ConversionResult>? appendMerger = null)
    {
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration, nameof(gptPrompterConfiguration));

        _appendMerger = appendMerger ?? MergeResults;
        _logger = GrITLoggerFactory.CreateLogger<InMemoryConversionResultsStore>();
        _gptPrompterConfiguration = gptPrompterConfiguration;
        _jobTracker = jobTracker;
    }

    /// <summary>
    /// Adds a new result for a job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="conversionResult">The conversion result to store.</param>
    /// <returns>A task that completes when the result is stored.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="jobId"/> is null or empty, or <paramref name="conversionResult"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown if an entry for <paramref name="jobId"/> already exists.</exception>
    /// <remarks>
    /// Before adding, the method enforces the configured max capacity
    /// (<see cref="GptChatGrxmlConfiguration.InMemoryConversionResultsStoreMaxItems"/>).
    /// When eviction is needed, the oldest item by <see cref="ConversionResult.CreatedAt"/> is removed and
    /// an attempt is made to remove its status via <see cref="JobTracker.RemoveStatus(string)"/>.
    /// </remarks>
    public async Task AddResultAsync(string jobId, ConversionResult conversionResult)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }

        ArgumentNullException.ThrowIfNull(conversionResult);

        _logger.LogInformation("[AddResultAsync] Start | JobId={JobId} | Items={Items}", jobId, conversionResult.ResultData?.Count ?? 0);

        await EnforceMaxSizeAsync();

        if (!_store.TryAdd(jobId, conversionResult))
            throw new InvalidOperationException($"Result for job '{jobId}' already exists.");

        return;
    }

    /// <summary>
    /// Appends data to an existing job result using the configured merge function.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="conversionResult">The additional result data to append.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="jobId"/> is null or empty, or <paramref name="conversionResult"/> is null.
    /// </exception>
    /// <remarks>
    /// If the job does not exist yet, it is created with <paramref name="conversionResult"/>.
    /// If it exists, the store merges the existing and new results via the merger provided in the constructor
    /// (or the default merger which concatenates values per file key with a newline).
    /// </remarks>
    public Task AppendResultAsync(string jobId, ConversionResult conversionResult)
    {
        _logger.LogInformation("[AppendResultAsync] Start | JobId={JobId} | Items={Items}", jobId, conversionResult?.ResultData?.Count ?? 0);

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        ArgumentNullException.ThrowIfNull(conversionResult);

        _store.AddOrUpdate(
            jobId,
            // if absent, just set the provided result
            conversionResult,
            // if present, merge existing + new via the configured merger
            (_, existing) => _appendMerger(existing, conversionResult));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces the existing result for a job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="conversionResult">The new result to set.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="jobId"/> is null or empty, or <paramref name="conversionResult"/> is null.
    /// </exception>
    /// <exception cref="KeyNotFoundException">Thrown if no result exists for <paramref name="jobId"/>.</exception>
    public Task UpdateResultAsync(string jobId, ConversionResult conversionResult)
    {
        _logger.LogInformation("[UpdateResultAsync] Start | JobId={JobId} | Items={Items}", jobId, conversionResult?.ResultData?.Count ?? 0);

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        ArgumentNullException.ThrowIfNull(conversionResult);

        if (!_store.ContainsKey(jobId))
            throw new KeyNotFoundException($"Result for job '{jobId}' not found.");

        _store[jobId] = conversionResult;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves the conversion result for a job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>
    /// The <see cref="ConversionResult"/> if found; otherwise, <c>null</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobId"/> is null or empty.</exception>
    public Task<ConversionResult?> GetResultAsync(string jobId)
    {
        _logger.LogInformation("[GetResultAsync] Start | JobId={JobId}", jobId);

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        _store.TryGetValue(jobId, out var result);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Checks whether a result exists for the given job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns><c>true</c> if a result exists; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobId"/> is null or empty.</exception>
    public Task<bool> ResultExistsAsync(string jobId)
    {
        _logger.LogInformation("[ResultExistsAsync] Start | JobId={JobId}", jobId);

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        return Task.FromResult(_store.ContainsKey(jobId));
    }

    /// <summary>
    /// Removes the stored result for a job, if it exists.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobId"/> is null or empty.</exception>
    /// <remarks>
    /// If the job entry does not exist, the method is a no-op.
    /// This method does not update the <see cref="JobTracker"/>; callers should handle status changes separately.
    /// </remarks>
    public Task RemoveResultAsync(string jobId)
    {
        _logger.LogInformation("[RemoveResultAsync] Start | JobId={JobId}", jobId);

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        _store.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }

    private static ConversionResult MergeResults(ConversionResult existingResult, ConversionResult newResult)
    {
        ArgumentNullException.ThrowIfNull(existingResult);
        ArgumentNullException.ThrowIfNull(newResult);

        if (newResult.ResultData != null)
        {
            foreach (var resultDataKey in newResult.ResultData.Keys)
            {
                if (existingResult.ResultData.TryGetValue(resultDataKey, out var existingEntry))
                {
                    existingResult.ResultData[resultDataKey] = string.Join("\n", existingEntry, newResult.ResultData[resultDataKey]);
                }
                else
                {
                    existingResult.ResultData[resultDataKey] = newResult.ResultData[resultDataKey];
                }
            }
        }

        return existingResult;
    }

    private async Task EnforceMaxSizeAsync()
    {
        while (_store.Count >= _gptPrompterConfiguration.Value.InMemoryConversionResultsStoreMaxItems)
        {
            _logger.LogInformation(
                "[EnforceMaxSizeAsync] Store size {CurrentSize} exceeds max size {MaxSize}. Removing oldest result.",
                _store.Count, _gptPrompterConfiguration.Value.InMemoryConversionResultsStoreMaxItems);
            var oldest = _store.Aggregate(
                (l, r) => l.Value.CreatedAt < r.Value.CreatedAt ? l : r).Key;
            _logger.LogInformation("[EnforceMaxSizeAsync] Oldest result to remove: {Oldest}", oldest);
            if (!_store.TryRemove(oldest, out _))
            {
                throw new InvalidOperationException($"Failed to remove oldest result '{oldest}' from store.");
            }
            if (!_jobTracker.RemoveStatus(oldest))
            {
                _logger.LogWarning("[EnforceMaxSizeAsync] Failed to remove job status for {JobId}. It may not exist.", oldest);
            }
            _logger.LogInformation("[EnforceMaxSizeAsync] Removed oldest result: {Oldest}", oldest);
            await Task.Yield(); // Yield to avoid blocking
        }
    }
}
