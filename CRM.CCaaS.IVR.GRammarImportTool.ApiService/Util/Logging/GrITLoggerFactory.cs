// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Util.Logging;

public static class GrITLoggerFactory
{
    public static ILoggerFactory? Instance { get; set; }

    public static ILogger<T> CreateLogger<T>()
    {
        return Instance!.CreateLogger<T>();
    }
}
