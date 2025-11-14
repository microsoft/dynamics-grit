// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;

public interface IBackgroundTaskQueue
{
    Task<bool> EnqueueAsync(JobTask jobTask, CancellationToken cancellationToken);
    Task<JobTask?> DequeueAsync(CancellationToken cancellationToken);
}
