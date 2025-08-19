using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.Background;

public class JobTaskTest
{
    [Fact]
    public void When_JobTaskCreated_WithValidStreamAndType_Then_PropertiesAreSetCorrectly()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        using var ms = new MemoryStream(data);

        var jobTask = new JobTask(ms, JobType.ConvertZipGrxml, null);

        Assert.NotNull(jobTask.Id);
        Assert.Equal(JobType.ConvertZipGrxml, jobTask.Type);
        Assert.Equal(data, jobTask.Payload);
        Assert.False(jobTask.IsReady);
        Assert.Null(jobTask.Worker);
    }

    [Fact]
    public void When_JobTaskCreated_WithWorker_Then_WorkerIsSet()
    {
        using var ms = new MemoryStream([5, 6, 7]);
        Func<CancellationToken, Task> worker = ct => Task.CompletedTask;

        var jobTask = new JobTask(ms, JobType.ConvertSingleGrxml, worker);

        Assert.Equal(JobType.ConvertSingleGrxml, jobTask.Type);
        Assert.Equal(new byte[] { 5, 6, 7 }, jobTask.Payload);
        Assert.Equal(worker, jobTask.Worker);
    }

    [Fact]
    public void When_JobTaskCreated_IdIsUnique()
    {
        using var ms1 = new MemoryStream([1]);
        using var ms2 = new MemoryStream([2]);

        var jobTask1 = new JobTask(ms1, JobType.Other, null);
        var jobTask2 = new JobTask(ms2, JobType.Other, null);

        Assert.NotEqual(jobTask1.Id, jobTask2.Id);
    }

    [Fact]
    public void When_IsReadySet_Then_ValueIsUpdated()
    {
        using var ms = new MemoryStream([1]);
        var jobTask = new JobTask(ms, JobType.Other, null);

        jobTask.IsReady = true;

        Assert.True(jobTask.IsReady);
    }
}
