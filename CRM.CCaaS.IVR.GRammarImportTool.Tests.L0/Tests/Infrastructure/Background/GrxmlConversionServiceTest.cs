using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests;
using CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Domain.Grxml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.Background;

[Collection("BaseTestCollection")]
public class GrxmlConversionServiceTest : IDisposable
{
    private bool _disposedValue;
    private readonly BaseTest _baseTest;

    public GrxmlConversionServiceTest(BaseTest baseTest)
    {
        _baseTest = baseTest ?? throw new ArgumentNullException(nameof(baseTest));
        if (_baseTest.ServiceProvider == null)
            throw new InvalidOperationException("ServiceProvider is not initialized.");

        _baseTest.LogProvider.Logger.Clear();
        _baseTest.JobTrackerMock.Invocations.Clear();
    }

    private class TestGrxmlConversionService(IBackgroundTaskQueue queue,
        IOptions<GptChatGrxmlConfiguration> opts,
        JobTracker tracker) : GrxmlConversionService(queue, opts, tracker)
    {
        public Task RunAsync(CancellationToken ct)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(600));
            return Task.Run(async () => await ExecuteAsync(ct), cts.Token);
        }
    }

    [Fact]
    public async Task When_ReadyJob_Completes_Then_StatusRunningThenCompleted()
    {
        var runningTcs = new TaskCompletionSource();
        var completedTcs = new TaskCompletionSource();

        _baseTest.JobTrackerMock
            .Setup(t => t.SetStatus(It.IsAny<string>(), JobStatus.Running))
            .Callback(() => runningTcs.TrySetResult());
        _baseTest.JobTrackerMock
            .Setup(t => t.SetStatus(It.IsAny<string>(), JobStatus.Completed))
            .Callback(() => completedTcs.TrySetResult());

        _baseTest.QueueMock.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobTask(GptChatGrxmlToMcsConverterTest.CreateZipStream("file.grxml", "file2.grxml"), JobType.ConvertZipGrxml,
            async (ct) => await Task.CompletedTask)
            { IsReady = true }
            )
            .ReturnsAsync(new JobTask(GptChatGrxmlToMcsConverterTest.CreateZipStream("file.grxml", "file2.grxml"), JobType.ConvertZipGrxml,
                async (ct) => await Task.FromException<Exception>(new InvalidOperationException("Test exception")))
            { IsReady = false }
            );

        using var service = new TestGrxmlConversionService(_baseTest.QueueMock.Object, Options.Create(_baseTest.GptChatGrxmlTestConfiguration),
            _baseTest.JobTrackerMock.Object);

        using var cts = new CancellationTokenSource();

        var runTask = service.RunAsync(cts.Token);

        await runningTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await completedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Cancel loop after first cycle
        cts.Cancel();
        await runTask;
        Assert.True(runTask.IsCompleted);

        var loggedMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(loggedMessages, m => m.Contains("GrxmlConversionService is stopping", StringComparison.OrdinalIgnoreCase));

        _baseTest.JobTrackerMock.Verify(t => t.SetStatus(It.IsAny<string>(), JobStatus.Running), Times.Once);
        _baseTest.JobTrackerMock.Verify(t => t.SetStatus(It.IsAny<string>(), JobStatus.Completed), Times.Once);
        _baseTest.JobTrackerMock.Verify(t => t.SetStatus(It.IsAny<string>(), JobStatus.Failed), Times.Never);
    }

    [Fact]
    public async Task When_ReadyJob_Fails_Then_StatusRunningThenFailed()
    {
        var runningTcs = new TaskCompletionSource();
        var failedTcs = new TaskCompletionSource();

        _baseTest.JobTrackerMock
            .Setup(t => t.SetStatus(It.IsAny<string>(), JobStatus.Running))
            .Callback(() => runningTcs.TrySetResult());
        _baseTest.JobTrackerMock
            .Setup(t => t.SetStatus(It.IsAny<string>(), JobStatus.Failed))
            .Callback(() => failedTcs.TrySetResult());

        _baseTest.QueueMock.SetupSequence(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobTask(GptChatGrxmlToMcsConverterTest.CreateZipStream("file.grxml", "file2.grxml"), JobType.ConvertZipGrxml,
            async (ct) => await Task.FromException<Exception>(new InvalidOperationException("Test exception")))
            { IsReady = true }
            )
            .ReturnsAsync(() =>
            {
                Task.Delay(1000).Wait(); // Simulate some delay before next job
                return new JobTask(GptChatGrxmlToMcsConverterTest.CreateZipStream("file.grxml", "file2.grxml"), JobType.ConvertZipGrxml,
                async (ct) => await Task.FromException<Exception>(new InvalidOperationException("Test exception")))
                { IsReady = false };
            });

        using var service = new TestGrxmlConversionService(_baseTest.QueueMock.Object, Options.Create(_baseTest.GptChatGrxmlTestConfiguration),
            _baseTest.JobTrackerMock.Object);

        using var cts = new CancellationTokenSource();

        var runTask = service.RunAsync(cts.Token);

        await runningTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await failedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Cancel loop after first cycle
        cts.Cancel();
        await runTask;
        Assert.True(runTask.IsCompleted);

        var loggedMessages = _baseTest.LogProvider.Logger.LoggedMessages;

        Assert.Contains(loggedMessages, m => m.Contains("GrxmlConversionService is stopping", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(loggedMessages, m => m.Contains("[ExecuteAsync] Error processing job", StringComparison.OrdinalIgnoreCase));

        _baseTest.JobTrackerMock.Verify(t => t.SetStatus(It.IsAny<string>(), JobStatus.Running), Times.Once);
        _baseTest.JobTrackerMock.Verify(t => t.SetStatus(It.IsAny<string>(), JobStatus.Failed), Times.Once);
        _baseTest.JobTrackerMock.Verify(t => t.SetStatus(It.IsAny<string>(), JobStatus.Completed), Times.Never);
    }

    [Fact]
    public async Task When_JobNotReady_Then_StatusNotChanged()
    {
        _baseTest.JobTrackerMock.Invocations.Clear();
        _baseTest.JobTrackerMock
            .Setup(t => t.SetStatus(It.IsAny<string>(), It.IsAny<JobStatus>()));

        _baseTest.QueueMock.Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobTask(GptChatGrxmlToMcsConverterTest.CreateZipStream("file.grxml", "file2.grxml"), JobType.ConvertZipGrxml,
            async (ct) => await Task.CompletedTask)
            { IsReady = false }
            );

        using var service = new TestGrxmlConversionService(_baseTest.QueueMock.Object, Options.Create(_baseTest.GptChatGrxmlTestConfiguration),
            _baseTest.JobTrackerMock.Object);

        using var cts = new CancellationTokenSource();

        var runTask = service.RunAsync(cts.Token);

        await Task.Delay(2000, cts.Token);

        // Cancel loop after first cycle
        cts.Cancel();
        await runTask;
        Assert.True(runTask.IsCompleted);

        var loggedMessages = _baseTest.LogProvider.Logger.LoggedMessages;
        Assert.Contains(loggedMessages, m => m.Contains("GrxmlConversionService is stopping", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(loggedMessages, m => m.Contains("Job is not ready", StringComparison.OrdinalIgnoreCase));

        _baseTest.JobTrackerMock.Verify(t => t.SetStatus(It.IsAny<string>(), It.IsAny<JobStatus>()), Times.Never);
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
    // ~GrxmlConversionServiceTest()
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
