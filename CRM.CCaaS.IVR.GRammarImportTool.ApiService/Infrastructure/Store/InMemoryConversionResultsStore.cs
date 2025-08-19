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
    /// Creates the store.
    /// appendMerger(old, added) => merged
    /// Default: last-write-wins (replace with added).
    /// </summary>
    public InMemoryConversionResultsStore(
        IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration,
        JobTracker jobTracker,
        Func<ConversionResult, ConversionResult, ConversionResult>? appendMerger = null)
    {
        ArgumentNullException.ThrowIfNull(gptPrompterConfiguration, nameof(gptPrompterConfiguration));

        _appendMerger = appendMerger ?? MergeResults;
        _logger = GrITLoggerFactory.CreateLogger<InMemoryConversionResultsStore>();
        _gptPrompterConfiguration = gptPrompterConfiguration;
        _jobTracker = jobTracker ?? throw new ArgumentNullException(nameof(jobTracker), "JobTracker cannot be null.");
    }

    /// <summary>
    /// Adds a new result. Throws if the job already exists.
    /// </summary>
    public async Task AddResultAsync(string jobId, ConversionResult conversationResult)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        ArgumentNullException.ThrowIfNull(conversationResult);

        await EnforceMaxSizeAsync();

        if (!_store.TryAdd(jobId, conversationResult))
            throw new InvalidOperationException($"Result for job '{jobId}' already exists.");

        return;
    }

    /// <summary>
    /// Appends to an existing result using the merge function.
    /// If no result exists for the job, it will create it.
    /// </summary>
    public Task AppendResultAsync(string jobId, ConversionResult conversationResult)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        ArgumentNullException.ThrowIfNull(conversationResult);

        _store.AddOrUpdate(
            jobId,
            // if absent, just set the provided result
            conversationResult,
            // if present, merge existing + new via the configured merger
            (_, existing) => _appendMerger(existing, conversationResult));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces the existing result. Throws if it doesn't exist.
    /// </summary>
    public Task UpdateResultAsync(string jobId, ConversionResult conversationResult)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        ArgumentNullException.ThrowIfNull(conversationResult);

        if (!_store.ContainsKey(jobId))
            throw new KeyNotFoundException($"Result for job '{jobId}' not found.");

        _store[jobId] = conversationResult;
        return Task.CompletedTask;
    }

    public Task<ConversionResult?> GetResultAsync(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        _store.TryGetValue(jobId, out var result);
        return Task.FromResult(result);
    }

    public Task<bool> ResultExistsAsync(string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "Job ID cannot be null or empty.");
        }
        return Task.FromResult(_store.ContainsKey(jobId));
    }

    public Task RemoveResultAsync(string jobId)
    {
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
        while (_store.Count >= _gptPrompterConfiguration.Value.ConversationResultsStoreInMemoryMaxItems)
        {
            _logger.LogInformation(
                "[EnforceMaxSizeAsync] Store size {CurrentSize} exceeds max size {MaxSize}. Removing oldest result.",
                _store.Count, _gptPrompterConfiguration.Value.ConversationResultsStoreInMemoryMaxItems);
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
