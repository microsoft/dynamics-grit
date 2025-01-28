var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.CRM_CCaaS_IVR_GRammarImportTool_ApiService>("apiservice");

builder.Build().Run();
