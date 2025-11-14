// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.Store;

[Collection("BaseTestCollection")]
public class FileConversionResultsStoreTest : IDisposable
{
    private readonly BaseTest _baseTest;
    private readonly ConcurrentBag<string> _tempDirs = new();
    private bool _disposedValue;

    public FileConversionResultsStoreTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        _baseTest.LogProvider.Logger.Clear();
    }

    private static ConversionResult CreateResult(params (string key, string value)[] entries)
    {
        var dict = new ConcurrentDictionary<string, string>();
        foreach (var (key, value) in entries)
            dict[key] = value;
        return new ConversionResult(dict) { CreatedAt = DateTime.UtcNow };
    }

    private (FileConversionResultsStore store, string baseDir, Mock<JobTracker> tracker) CreateStore(
        int maxItems = 10,
        bool hashFileNames = false)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "GrIT_FileStoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        _tempDirs.Add(baseDir);

        var config = new GptChatGrxmlConfiguration
        {
            FileConversationResultsStorePath = baseDir,
            FileConversationResultsStoreMaxItems = maxItems,
            HashFileNameInLogs = hashFileNames
        };
        var tracker = new Mock<JobTracker>();
        tracker.Setup(t => t.RemoveStatus(It.IsAny<string>())).Returns(true);
        var store = new FileConversionResultsStore(Options.Create(config), tracker.Object);
        return (store, baseDir, tracker);
    }

    [Fact]
    public async Task When_AddResultAsync_WritesFiles_And_ResultExistsAndGetReturnContent()
    {
        var (store, baseDir, _) = CreateStore();
        var jobId = "job1";
        var result = CreateResult(("file1.txt", "content1"), ("file2.json", "content2"));

        await store.AddResultAsync(jobId, result);

        var jobFolder = Path.Combine(baseDir, jobId);
        Assert.True(Directory.Exists(jobFolder));
        Assert.True(File.Exists(Path.Combine(jobFolder, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(jobFolder, "file2.json")));

        Assert.True(await store.ResultExistsAsync(jobId));

        var loaded = await store.GetResultAsync(jobId);
        Assert.NotNull(loaded);
        // Keys are file names without extension in GetResultAsync
        Assert.Equal("content1", loaded!.ResultData["file1"]);
        Assert.Equal("content2", loaded.ResultData["file2"]);
    }

    [Fact]
    public async Task When_AddResultAsync_EmptyJobID_Then_Throws_exception()
    {
        var (store, baseDir, _) = CreateStore();
        var jobId = "";
        var result = CreateResult(("file1.txt", "content1"), ("file2.json", "content2"));

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.AddResultAsync(jobId, result));
    }

    [Fact]
    public async Task When_AddResultAsync_TooLongJobID_Then_Throws_exception()
    {
        var (store, baseDir, _) = CreateStore();
        var jobId = new string('a', 256);
        var result = CreateResult(("file1.txt", "content1"), ("file2.json", "content2"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.AddResultAsync(jobId, result));
    }

    [Fact]
    public async Task When_AppendResultAsync_NewFile_Then_AddResult()
    {
        var (store, baseDir, _) = CreateStore();
        var jobId = "job-append";
        await store.AddResultAsync(jobId, CreateResult(("a.txt", "v1")));

        await store.AppendResultAsync(jobId, CreateResult(("b.txt", "v1")));

        var jobFolder = Path.Combine(baseDir, jobId);
        var contentA = await File.ReadAllTextAsync(Path.Combine(jobFolder, "a.txt"));
        Assert.Equal("v1", contentA); // unchanged
        var contentB = await File.ReadAllTextAsync(Path.Combine(jobFolder, "b.txt"));
        Assert.Equal("v1", contentB); // added      
    }

    [Fact]
    public async Task When_AppendResultAsync_EmptyJobID_Then_Throws_exception()
    {
        var (store, baseDir, _) = CreateStore();
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.AppendResultAsync("", CreateResult(("a.txt", "v2"))));
    }

    [Fact]
    public async Task When_GetResultAsync_Missing_Then_ReturnsNull()
    {
        var (store, _, _) = CreateStore();
        var result = await store.GetResultAsync("no-such-job");
        Assert.Null(result);
    }

    [Fact]
    public async Task When_RemoveResultAsync_Existing_Then_Deleted()
    {
        var (store, baseDir, _) = CreateStore();
        var jobId = "job-rem";
        await store.AddResultAsync(jobId, CreateResult(("a.txt", "1")));

        await store.RemoveResultAsync(jobId);

        var jobFolder = Path.Combine(baseDir, jobId);
        Assert.False(Directory.Exists(jobFolder));
        Assert.False(await store.ResultExistsAsync(jobId));
        Assert.Null(await store.GetResultAsync(jobId));
    }

    [Fact]
    public async Task When_UpdateResultAsync_Existing_Then_Replaced()
    {
        var (store, baseDir, _) = CreateStore();
        var jobId = "job-upd";
        await store.AddResultAsync(jobId, CreateResult(("a.txt", "1")));

        await store.UpdateResultAsync(jobId, CreateResult(("a.txt", "2")));

        var content = await File.ReadAllTextAsync(Path.Combine(baseDir, jobId, "a.txt"));
        Assert.Equal("2", content);
    }

    [Fact]
    public async Task When_UpdateResultAsync_Missing_Then_DirectoryNotFoundException()
    {
        var (store, _, _) = CreateStore();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            store.UpdateResultAsync("missing", CreateResult(("a.txt", "2"))));
    }

    [Fact]
    public async Task When_PreexistingExceedsCapacity_Then_OldestEvictedAndJobTrackerUpdated()
    {
        var (store, baseDir, tracker) = CreateStore(maxItems: 1);
        // Pre-create folders so EnforceMaxSizeAsync sees > max before write
        var oldA = Path.Combine(baseDir, "job-oldA");
        var oldB = Path.Combine(baseDir, "job-oldB");
        Directory.CreateDirectory(oldA);
        Directory.CreateDirectory(oldB);
        // Make job-oldA the oldest
        Directory.SetCreationTimeUtc(oldA, DateTime.UtcNow.AddMinutes(-10));
        Directory.SetCreationTimeUtc(oldB, DateTime.UtcNow.AddMinutes(-5));

        tracker.Setup(t => t.RemoveStatus(It.IsAny<string>())).Returns(true);

        await store.AddResultAsync("job-new", CreateResult(("x.txt", "x")));

        Assert.False(Directory.Exists(oldA)); // evicted
        Assert.True(Directory.Exists(oldB));
        Assert.True(Directory.Exists(Path.Combine(baseDir, "job-new")));

        tracker.Verify(t => t.RemoveStatus("job-oldA"), Times.Once);
    }

    [Fact]
    public async Task When_PreexistingExceedsCapacity_and_CleanUp_TimeoutThen_ExceptionThrown()
    {
        var (store, baseDir, tracker) = CreateStore(maxItems: 1);
        // Pre-create folders so EnforceMaxSizeAsync sees > max before write
        var oldA = Path.Combine(baseDir, "job-timeout");
        var oldB = Path.Combine(baseDir, "job-oldB");
        Directory.CreateDirectory(oldA);
        Directory.CreateDirectory(oldB);
        // Make job-oldA the oldest
        Directory.SetCreationTimeUtc(oldA, DateTime.UtcNow.AddMinutes(-10));
        Directory.SetCreationTimeUtc(oldB, DateTime.UtcNow.AddMinutes(-5));

        tracker.Setup(t => t.RemoveStatus(It.IsAny<string>())).Returns(() => throw new OperationCanceledException("timeout remove status"));

        await store.AddResultAsync("job-new", CreateResult(("x.txt", "x")));

        var logs = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logs, m => m.Contains("[EnforceMaxSizeAsync] operation timed out", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_EvictionRemoveStatusFails_Then_WarningLogged()
    {
        var (store, baseDir, tracker) = CreateStore(maxItems: 1);
        var oldest = Path.Combine(baseDir, "oldest");
        var newer = Path.Combine(baseDir, "newer");
        Directory.CreateDirectory(oldest);
        Directory.CreateDirectory(newer);
        Directory.SetCreationTimeUtc(oldest, DateTime.UtcNow.AddMinutes(-10));
        Directory.SetCreationTimeUtc(newer, DateTime.UtcNow.AddMinutes(-5));

        tracker.Setup(t => t.RemoveStatus(It.IsAny<string>())).Returns(false);

        await store.AddResultAsync("job-new", CreateResult(("a.txt", "1")));

        // Oldest folder removed
        Assert.False(Directory.Exists(oldest));
        var logs = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logs, m => m.Contains("Failed to remove job status", StringComparison.OrdinalIgnoreCase));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            // Cleanup temp directories
            foreach (var dir in _tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch
                {
                    // ignore cleanup failures
                }
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
