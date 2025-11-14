// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.GptChat;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Grxml;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Grxml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.Background;

[Collection("BaseTestCollection")]
public class BackgroundTaskQueueTest : IDisposable
{
    private bool _disposedValue;
    private readonly BackgroundTaskQueue _queue;
    private readonly BaseTest _baseTest;
    private readonly Mock<IGptChat> _gptChatMock = new();
    private readonly MemoryStream _grxmlStream;

    public BackgroundTaskQueueTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        _queue = new BackgroundTaskQueue(_baseTest.ServiceProvider.GetRequiredService<IOptions<GptChatGrxmlConfiguration>>(), _baseTest.ServiceProvider);

        var grxmlContent = "<test>grxml</test>";
        _grxmlStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(grxmlContent));
    }

    [Fact]
    public async Task When_EnqueueAsync_WithValidJobTask_ReturnsTrue()
    {
        using var ms = new MemoryStream([1, 2, 3]);
        var jobTask = new JobTask(ms, JobType.ConvertZipGrxml, null);

        var result = await _queue.EnqueueAsync(jobTask, new CancellationToken());

        Assert.True(result);
    }

    [Fact]
    public async Task When_EnqueueAsync_and_QueueIsFull_Then_ReturnsFalse()
    {
        using var ms1 = new MemoryStream([1]);
        using var ms2 = new MemoryStream([2]);
        using var ms3 = new MemoryStream([3]);

        var jobTask1 = new JobTask(ms1, JobType.ConvertZipGrxml, null);
        var jobTask2 = new JobTask(ms2, JobType.ConvertZipGrxml, null);
        var jobTask3 = new JobTask(ms3, JobType.ConvertZipGrxml, null);

        // Fill the queue
        await _queue.EnqueueAsync(jobTask1, new CancellationToken());
        await _queue.EnqueueAsync(jobTask2, new CancellationToken());

        // This should timeout and return false
        var result = await _queue.EnqueueAsync(jobTask3, new CancellationToken());

        Assert.False(result);
    }

    [Fact]
    public async Task When_DequeueAsync_Then_ReturnsJobTaskAndSetsWorker()
    {
        using var ms = new MemoryStream([1, 2, 3]);
        var jobTask = new JobTask(ms, JobType.ConvertSingleGrxml, null);

        await _queue.EnqueueAsync(jobTask, new CancellationToken());

        var dequeued = await _queue.DequeueAsync(CancellationToken.None);

        Assert.NotNull(dequeued);
        Assert.Equal(jobTask.Id, dequeued.Id);
        Assert.True(dequeued.IsReady);
        Assert.NotNull(dequeued.Worker);
    }

    [Fact]
    public async Task When_DequeueAsync_and_UnknownJobType_Then_SetsNoWorker()
    {
        using var ms = new MemoryStream([1, 2, 3]);
        var jobTask = new JobTask(ms, JobType.Other, null);

        await _queue.EnqueueAsync(jobTask, new CancellationToken());

        var dequeued = await _queue.DequeueAsync(CancellationToken.None);

        Assert.NotNull(dequeued);
        Assert.Equal(jobTask.Id, dequeued.Id);
        Assert.Null(dequeued.Worker);
        Assert.False(dequeued.IsReady);
    }

    [Fact]
    public async Task When_DequeueAsync_Then_ConvertZipGrxmlJobProcessesResults()
    {
        using var ms = GptChatGrxmlToMcsConverterTest.CreateZipStream("file1.grxml", "file2.grxml");
        var jobTask = new JobTask(ms, JobType.ConvertZipGrxml, null);

        _gptChatMock
            .Setup(x => x.ConvertZipAsync(It.IsAny<Stream>(), It.IsAny<Channel<KeyValuePair<string, string>>>()))
            .Returns(async (Stream s, Channel<KeyValuePair<string, string>> ch) =>
            {
                await ch.Writer.WriteAsync(new KeyValuePair<string, string>("file.yaml", "yaml-content"));
                ch.Writer.Complete();
                return "done";
            });

        await _queue.EnqueueAsync(jobTask, new CancellationToken());
        var dequeued = await _queue.DequeueAsync(CancellationToken.None);

        Assert.NotNull(dequeued);
        Assert.NotNull(dequeued.Worker);
        await dequeued.Worker!(CancellationToken.None);

        _baseTest.ResultsStoreMock.Verify(x => x.AddResultAsync(dequeued.Id, It.IsAny<ConversionResult>()), Times.Once);
    }

    [Fact]
    public async Task When_DequeueAsync_Then_ConvertSingleGrxmlJob_ProcessesResults()
    {
        var jobTask = new JobTask(_grxmlStream!, JobType.ConvertSingleGrxml, null);

        _gptChatMock
            .Setup(x => x.ConvertFileAsync(It.IsAny<string>()))
            .ReturnsAsync("yaml: content");

        await _queue.EnqueueAsync(jobTask, new CancellationToken());
        var dequeued = await _queue.DequeueAsync(CancellationToken.None);

        Assert.NotNull(dequeued);
        Assert.NotNull(dequeued.Worker);
        await dequeued.Worker!(CancellationToken.None);

        _baseTest.ResultsStoreMock.Verify(x => x.AddResultAsync(dequeued.Id, It.IsAny<ConversionResult>()), Times.Once);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~BackgroundTaskQueueTest()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
