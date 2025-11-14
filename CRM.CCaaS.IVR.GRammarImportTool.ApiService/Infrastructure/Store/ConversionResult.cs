// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Concurrent;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;

public class ConversionResult(ConcurrentDictionary<string, string> resultData)
{
    public ConcurrentDictionary<string, string> ResultData { get; set; } = resultData;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
