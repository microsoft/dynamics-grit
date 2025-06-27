using Aspire.Hosting;

Console.WriteLine($"Starting Aspire application with args: {string.Join(", ", args)}");

var builder = DistributedApplication.CreateBuilder(args);

var environmentArg = args.FirstOrDefault(a => a.StartsWith("--environment=", StringComparison.OrdinalIgnoreCase));
if (environmentArg != null)
{
    Console.WriteLine($"Environment set to: {environmentArg}");

    environmentArg = environmentArg.Substring("--environment=".Length);
    Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environmentArg);
    builder.Environment.EnvironmentName = environmentArg;
}

var apiService = builder.AddProject<Projects.CRM_CCaaS_IVR_GRammarImportTool_ApiService>("apiservice")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName);

builder.Build().Run();
