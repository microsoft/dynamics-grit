using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
public class InMemoryConversionResultsStoreTest : IDisposable
{
    private readonly BaseTest _baseTest;
    private bool _disposedValue;

    public InMemoryConversionResultsStoreTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        _baseTest.LogProvider.Logger.Clear();
    }

    private static InMemoryConversionResultsStore CreateStore(
        int maxItems,
        Mock<JobTracker>? trackerMock = null,
        Func<ConversionResult, ConversionResult, ConversionResult>? customMerger = null)
    {
        var config = new GptChatGrxmlConfiguration
        {
            InMemoryConversionResultsStoreMaxItems = maxItems
        };
        return new InMemoryConversionResultsStore(
            Options.Create(config),
            (trackerMock ?? new Mock<JobTracker>()).Object,
            customMerger);
    }

    private static ConversionResult CreateResult(params (string key, string value)[] entries)
    {
        var dict = new ConcurrentDictionary<string, string>();
        foreach (var (key, value) in entries)
            dict[key] = value;
        return new ConversionResult(dict) { CreatedAt = DateTime.UtcNow };
    }

    [Fact]
    public async Task When_AddResultAsync_NewId_Then_ResultStored()
    {
        var tracker = new Mock<JobTracker>();
        var store = CreateStore(10, tracker);
        var jobId = "job1";
        var result = CreateResult(("file1.yaml", "content1"));

        await store.AddResultAsync(jobId, result);
        var loaded = await store.GetResultAsync(jobId);

        Assert.NotNull(loaded);
        Assert.Equal("content1", loaded!.ResultData["file1.yaml"]);
        Assert.True(await store.ResultExistsAsync(jobId));
    }

    [Fact]
    public async Task When_AddResultAsync_DuplicateId_Then_InvalidOperationException()
    {
        var store = CreateStore(10);
        var jobId = "dup";
        await store.AddResultAsync(jobId, CreateResult(("a", "b")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddResultAsync(jobId, CreateResult(("c", "d"))));
    }

    [Fact]
    public async Task When_AppendResultAsync_NewId_Then_ResultCreated()
    {
        var store = CreateStore(10);
        var jobId = "job-new";

        await store.AppendResultAsync(jobId, CreateResult(("k1", "v1")));
        var loaded = await store.GetResultAsync(jobId);

        Assert.NotNull(loaded);
        Assert.Equal("v1", loaded!.ResultData["k1"]);
    }

    [Fact]
    public async Task When_AppendResultAsync_ExistingId_Then_ValuesMerged()
    {
        var store = CreateStore(10);
        var jobId = "job-merge";
        var initial = CreateResult(("f1", "line1"));
        await store.AddResultAsync(jobId, initial);

        await store.AppendResultAsync(jobId, CreateResult(("f1", "line2"), ("f2", "other")));
        var merged = await store.GetResultAsync(jobId);

        Assert.NotNull(merged);
        Assert.Equal("line1\nline2", merged!.ResultData["f1"]);
        Assert.Equal("other", merged.ResultData["f2"]);
    }

    [Fact]
    public async Task When_UpdateResultAsync_NonExisting_Then_KeyNotFoundException()
    {
        var store = CreateStore(10);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            store.UpdateResultAsync("missing", CreateResult(("k", "v"))));
    }

    [Fact]
    public async Task When_UpdateResultAsync_Existing_Then_Replaced()
    {
        var store = CreateStore(10);
        var jobId = "job-upd";
        await store.AddResultAsync(jobId, CreateResult(("a", "1")));

        await store.UpdateResultAsync(jobId, CreateResult(("b", "2")));
        var loaded = await store.GetResultAsync(jobId);

        Assert.NotNull(loaded);
        Assert.False(loaded!.ResultData.ContainsKey("a"));
        Assert.Equal("2", loaded.ResultData["b"]);
    }

    [Fact]
    public async Task When_RemoveResultAsync_Existing_Then_Deleted()
    {
        var store = CreateStore(10);
        var jobId = "job-rem";
        await store.AddResultAsync(jobId, CreateResult(("a", "1")));

        await store.RemoveResultAsync(jobId);

        Assert.False(await store.ResultExistsAsync(jobId));
        Assert.Null(await store.GetResultAsync(jobId));
    }

    [Fact]
    public async Task When_GetResultAsync_Missing_Then_ReturnsNull()
    {
        var store = CreateStore(5);
        var result = await store.GetResultAsync("none");
        Assert.Null(result);
    }

    [Fact]
    public async Task When_AddBeyondCapacity_Then_OldestEvictedAndJobTrackerUpdated()
    {
        var tracker = new Mock<JobTracker>();
        tracker.Setup(t => t.RemoveStatus(It.IsAny<string>())).Returns(true);

        var store = CreateStore(2, tracker);

        var r1 = CreateResult(("k1", "v1"));
        r1.CreatedAt = DateTime.UtcNow.AddMinutes(-10); // oldest
        var r2 = CreateResult(("k2", "v2"));
        r2.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        var r3 = CreateResult(("k3", "v3"));

        await store.AddResultAsync("job1", r1);
        await store.AddResultAsync("job2", r2);
        await store.AddResultAsync("job3", r3); // triggers eviction

        var exists1 = await store.ResultExistsAsync("job1");
        var exists2 = await store.ResultExistsAsync("job2");
        var exists3 = await store.ResultExistsAsync("job3");

        Assert.False(exists1);              // evicted
        Assert.True(exists2);
        Assert.True(exists3);

        tracker.Verify(t => t.RemoveStatus("job1"), Times.Once);
    }

    [Fact]
    public async Task When_AddBeyondCapacity_RemoveStatusFails_Then_WarningLogged()
    {
        var tracker = new Mock<JobTracker>();
        tracker.Setup(t => t.RemoveStatus(It.IsAny<string>())).Returns(false);

        var store = CreateStore(1, tracker); // capacity 1 to trigger quicker

        var r1 = CreateResult(("k1", "v1"));
        r1.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        var r2 = CreateResult(("k2", "v2"));

        await store.AddResultAsync("job-old", r1);
        await store.AddResultAsync("job-new", r2);

        // Assert eviction happened
        Assert.False(await store.ResultExistsAsync("job-old"));
        Assert.True(await store.ResultExistsAsync("job-new"));

        // Inspect logs for warning
        var logs = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(logs, m => m.Contains("Failed to remove job status", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_CustomMergerProvided_Then_UsesCustomLogic()
    {
        var tracker = new Mock<JobTracker>();
        // Custom merger overwrites existing keys with UPPER appended marker
        ConversionResult CustomMerge(ConversionResult existing, ConversionResult added)
        {
            foreach (var kv in added.ResultData)
                existing.ResultData[kv.Key] = kv.Value.ToUpperInvariant() + "_MERGED";
            return existing;
        }

        var store = CreateStore(5, tracker, CustomMerge);
        await store.AddResultAsync("jobX", CreateResult(("a", "alpha")));
        await store.AppendResultAsync("jobX", CreateResult(("a", "beta"), ("b", "bee")));

        var loaded = await store.GetResultAsync("jobX");
        Assert.NotNull(loaded);
        Assert.Equal("BETA_MERGED", loaded!.ResultData["a"]);
        Assert.Equal("BEE_MERGED", loaded.ResultData["b"]);
    }

    [Fact]
    public async Task When_InvalidArguments_Then_ArgumentNullException()
    {
        var store = CreateStore(2);
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddResultAsync("", CreateResult(("a", "b"))));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddResultAsync("id1", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AppendResultAsync("", CreateResult(("a", "b"))));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.UpdateResultAsync("", CreateResult(("a", "b"))));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.GetResultAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.ResultExistsAsync(""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.RemoveResultAsync(""));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
            _disposedValue = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
