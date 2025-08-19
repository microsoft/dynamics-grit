using System;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;
using Xunit;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Tests.Infrastructure.Background;

public class JobTrackerTest
{
    [Fact]
    public void When_SetStatus_Then_GetStatusReturnsSetValue()
    {
        var tracker = new JobTracker();
        var jobId = Guid.NewGuid().ToString();

        tracker.SetStatus(jobId, JobStatus.Pending);

        Assert.Equal(JobStatus.Pending, tracker.GetStatus(jobId));
    }

    [Fact]
    public void When_SetStatusMultipleTimes_Then_GetStatusReturnsLatest()
    {

        var tracker = new JobTracker();
        var jobId = Guid.NewGuid().ToString();

        tracker.SetStatus(jobId, JobStatus.Pending);
        tracker.SetStatus(jobId, JobStatus.Running);
        tracker.SetStatus(jobId, JobStatus.Completed);

        Assert.Equal(JobStatus.Completed, tracker.GetStatus(jobId));
    }

    [Fact]
    public void When_GetStatusForUnknownJob_Then_ReturnsNull()
    {

        var tracker = new JobTracker();
        var unknownJobId = Guid.NewGuid().ToString();

        var status = tracker.GetStatus(unknownJobId);

        Assert.Null(status);
    }

    [Fact]
    public void When_SetStatus_WithDifferentJobIds_Then_TracksEachIndependently()
    {
        var tracker = new JobTracker();
        var jobId1 = Guid.NewGuid().ToString();
        var jobId2 = Guid.NewGuid().ToString();

        tracker.SetStatus(jobId1, JobStatus.Pending);
        tracker.SetStatus(jobId2, JobStatus.Failed);

        Assert.Equal(JobStatus.Pending, tracker.GetStatus(jobId1));
        Assert.Equal(JobStatus.Failed, tracker.GetStatus(jobId2));
    }
}
