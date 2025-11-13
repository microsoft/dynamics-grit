// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Concurrent;
using System.IO;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;
using Microsoft.Extensions.Options;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;

public class FileConversionResultsStore : IConversionResultsStore
{
    public const string SERVICE_KEY = "FileConversionResultsStore";
    private const int MAX_CLEAN_UP_WAIT_TIME_SEC = 60;
    private const int MAX_RESULT_WRITE_WAIT_TIME_SEC = 60;

    private readonly ILogger<FileConversionResultsStore> _logger;
    private readonly IOptions<GptChatGrxmlConfiguration> _gptPrompterConfiguration;
    private readonly JobTracker _jobTracker;
    private readonly string _baseDirectory;

    /// <summary>
    /// Initializes a new instance of the file-based conversion results store.
    /// Ensures the base directory exists.
    /// </summary>
    /// <param name="gptPrompterConfiguration">Configuration providing path and capacity limits.</param>
    /// <param name="jobTracker">Job tracker used to remove job statuses when folders are evicted.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if base directory path is empty or too long.</exception>
    /// <exception cref="IOException">Thrown if directory creation fails due to I/O errors.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the process lacks permissions to create directories.</exception>
    public FileConversionResultsStore(IOptions<GptChatGrxmlConfiguration> gptPrompterConfiguration,
        JobTracker jobTracker)
    {
        _logger = GrITLoggerFactory.CreateLogger<FileConversionResultsStore>();
        _jobTracker = jobTracker;
        _gptPrompterConfiguration = gptPrompterConfiguration;

        _logger.LogInformation("FileConversationStore initialized");
        _baseDirectory = _gptPrompterConfiguration.Value.FileConversationResultsStorePath;

        EnsureDirectoryExists(_baseDirectory);
    }

    /// <summary>
    /// Adds a new result for the specified job by writing files to a job-specific folder.
    /// Enforces store capacity before writing.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="conversionResult">The conversion result content to persist.</param>
    /// <returns>A task that completes when all files are written.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="jobId"/> is null or empty, or <paramref name="conversionResult"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the target path is invalid.</exception>
    /// <exception cref="IOException">Thrown if writing files fails due to I/O errors.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the process lacks permissions to write files.</exception>
    public async Task AddResultAsync(string jobId, ConversionResult conversionResult)
    {
        _logger.LogInformation("[AddResultAsync] Start | JobId={JobId}", jobId);

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "[AddResultAsync] Job ID cannot be null or empty.");
        }
        ArgumentNullException.ThrowIfNull(conversionResult);

        await EnforceMaxSizeAsync();

        string jobFolder = GetJobFolder(jobId);

        EnsureDirectoryExists(jobFolder);
        await WriteResultAsync(jobFolder, conversionResult);
    }

    /// <summary>
    /// Appends new result files for a job without overwriting existing files.
    /// Files that already exist are skipped.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="conversionResult">The additional result content to persist.</param>
    /// <returns>A task that completes when appends finish.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="jobId"/> is null or empty, or <paramref name="conversionResult"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the target path is invalid.</exception>
    /// <exception cref="IOException">Thrown if writing files fails due to I/O errors.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the process lacks permissions to write files.</exception>
    public async Task AppendResultAsync(string jobId, ConversionResult conversionResult)
    {
        _logger.LogInformation("[AppendResultAsync] Start | JobId={JobId} | Items={Items}",
            jobId, conversionResult?.ResultData?.Count ?? 0);

        if (string.IsNullOrEmpty(jobId))
        {
            throw new ArgumentNullException(nameof(jobId), "[AppendResultAsync] Job ID cannot be null or empty.");
        }
        ArgumentNullException.ThrowIfNull(conversionResult);

        string jobFolder = GetJobFolder(jobId);

        EnsureDirectoryExists(jobFolder);

        foreach (var kvp in conversionResult.ResultData)
        {
            string filePath = Path.Combine(jobFolder, kvp.Key);
            if (!File.Exists(filePath))
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(MAX_RESULT_WRITE_WAIT_TIME_SEC));
                await File.WriteAllTextAsync(filePath, kvp.Value, cts.Token);
            }
            else
            {
                _logger.LogInformation("[AppendResultAsync] skipping overwrite on Append");
            }
        }
    }

    /// <summary>
    /// Reads and returns the persisted result for the specified job by loading all files from the job folder.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>
    /// The <see cref="ConversionResult"/> if the job folder exists and files could be read; otherwise, null.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobId"/> is null.</exception>
    /// <exception cref="IOException">Thrown if reading files fails due to I/O errors.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the process lacks permissions to read files.</exception>
    public async Task<ConversionResult?> GetResultAsync(string jobId)
    {
        _logger.LogInformation("[GetResultAsync] Start | JobId={JobId}", jobId);

        string jobFolder = GetJobFolder(jobId);
        if (!Directory.Exists(jobFolder))
        {
            _logger.LogWarning("[GetResultAsync] No results found for JobId={JobId}", jobId);
            return null;
        }

        var hashEnabled = _gptPrompterConfiguration.Value.HashFileNameInLogs;
        var resultData = new ConcurrentDictionary<string, string>();
        foreach (var file in Directory.GetFiles(jobFolder, "*.*"))
        {
            var fileNameToLog = hashEnabled ? HashHelper.HashSha256Hex(Path.GetFileNameWithoutExtension(file)) : Path.GetFileNameWithoutExtension(file);
            var fileNameLogLabel = hashEnabled ? "HashedFileName" : "FileName";
            _logger.LogInformation("[GetResultAsync] Reading results {fileNameLogLabel}={fileNameToLog}", fileNameLogLabel, fileNameToLog);

            string key = Path.GetFileNameWithoutExtension(file);
            string value = await File.ReadAllTextAsync(file);
            resultData[key] = value;
        }

        return new ConversionResult(resultData);
    }

    /// <summary>
    /// Deletes the job folder and all contained files for the specified job.
    /// No-op if the job folder does not exist.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobId"/> is null.</exception>
    /// <exception cref="IOException">Thrown if delete operation fails due to I/O errors.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the process lacks permissions to delete files/folders.</exception>
    public Task RemoveResultAsync(string jobId)
    {
        _logger.LogInformation("[RemoveResultAsync] Start | JobId={JobId}", jobId);

        string jobFolder = GetJobFolder(jobId);
        if (Directory.Exists(jobFolder))
            Directory.Delete(jobFolder, true);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks whether a job folder exists for the specified job ID.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>True if the job folder exists; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobId"/> is null.</exception>
    public Task<bool> ResultExistsAsync(string jobId)
    {
        _logger.LogInformation("[ResultExistsAsync] Start | JobId={JobId}", jobId);

        string jobFolder = GetJobFolder(jobId);
        return Task.FromResult(Directory.Exists(jobFolder));
    }

    /// <summary>
    /// Replaces all existing files for the specified job with the provided result content.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="conversionResult">The result content to persist.</param>
    /// <returns>A task that completes when files are written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="conversionResult"/> is null.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobId"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown if the job folder does not exist.</exception>
    /// <exception cref="IOException">Thrown if writing files fails due to I/O errors.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown if the process lacks permissions to write files.</exception>
    public async Task UpdateResultAsync(string jobId, ConversionResult conversionResult)
    {
        ArgumentNullException.ThrowIfNull(conversionResult, nameof(conversionResult));

        _logger.LogInformation("[UpdateResultAsync] Start | JobId={JobId}", jobId);

        string jobFolder = GetJobFolder(jobId);
        if (!Directory.Exists(jobFolder))
            throw new DirectoryNotFoundException($"No result found for jobId '{jobId}'.");

        await WriteResultAsync(jobFolder, conversionResult);
    }

    private async Task EnforceMaxSizeAsync(int maxWaitTime = MAX_CLEAN_UP_WAIT_TIME_SEC)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(maxWaitTime));
        try
        {
            var cleanUpTask = Task.Run(() =>
            {
                var jobFolders = Directory.GetDirectories(_baseDirectory);
                while (jobFolders.Length > _gptPrompterConfiguration.Value.FileConversationResultsStoreMaxItems)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    var oldestFolder = jobFolders
                        .Select(path => new DirectoryInfo(path))
                        .OrderBy(dir => dir.CreationTimeUtc)
                        .First();

                    Directory.Delete(oldestFolder.FullName, recursive: true);
                    _logger.LogInformation("[EnforceMaxSizeAsync] Deleted oldest job folder: {Folder}", oldestFolder.FullName);
                    if (!_jobTracker.RemoveStatus(oldestFolder.Name))
                    {
                        _logger.LogWarning("[EnforceMaxSizeAsync] Failed to remove job status for {JobId}. It may not exist.", oldestFolder.Name);
                    }
                    jobFolders = Directory.GetDirectories(_baseDirectory);
                }

                _logger.LogInformation("[EnforceMaxSizeAsync] Current folder count: {FolderCount}. Max allowed: {MaxItems}.",
                    jobFolders.Length, _gptPrompterConfiguration.Value.FileConversationResultsStoreMaxItems);
            }, cts.Token);

            await cleanUpTask;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[EnforceMaxSizeAsync] operation timed out.");
        }
    }

    private string GetJobFolder(string jobId) => Path.Combine(_baseDirectory, jobId);

    private void EnsureDirectoryExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogError("[EnsureDirectoryExists] path '{Path}' is empty", path);
            throw new ArgumentOutOfRangeException(path);
        }
        if (path.Length > 255)
        {
            _logger.LogError("[EnsureDirectoryExists] path '{Path}' too long", path);
            throw new ArgumentOutOfRangeException(path);
        }

        if (!Directory.Exists(path))
        {
            var di = Directory.CreateDirectory(path);
            _logger.LogInformation("[EnsureDirectoryExists] directory {Path} created", di.FullName);
        }
        else
        {
            _logger.LogInformation("[EnsureDirectoryExists] directory {Path} already exists", path);
        }
    }

    private async Task WriteResultAsync(string jobFolder, ConversionResult conversionResult)
    {
        foreach (var kvp in conversionResult.ResultData)
        {
            string sanitizedFileName = Path.GetFileName(kvp.Key); // Sanitize the file name to prevent directory traversal
            string filePath = Path.Combine(jobFolder, sanitizedFileName);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(MAX_RESULT_WRITE_WAIT_TIME_SEC));
            await File.WriteAllTextAsync(filePath, kvp.Value, cts.Token);
        }
    }
}

