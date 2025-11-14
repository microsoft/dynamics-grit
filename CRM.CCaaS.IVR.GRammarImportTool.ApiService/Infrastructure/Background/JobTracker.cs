// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Concurrent;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;

public enum JobStatus { Pending, Running, Completed, Failed }

public class JobTracker
{
    private readonly ConcurrentDictionary<string, JobStatus> _statusMap = new();

    public virtual void SetStatus(string jobId, JobStatus status) => _statusMap[jobId] = status;

    public virtual JobStatus? GetStatus(string jobId) =>
        _statusMap.TryGetValue(jobId, out var status) ? status : null;

    public virtual bool RemoveStatus(string jobId) => _statusMap.TryRemove(jobId, out _);
}
