// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Common;
public class UnitTestHostEnvironment : IHostEnvironment
{
    public string ApplicationName { get; set; } = "GrITUnitTest";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(Directory.GetCurrentDirectory());
    public string EnvironmentName { get; set; } = Environments.Development;
}
