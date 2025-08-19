using System.Text.Json.Serialization;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Background;

public enum JobType
{
    ConvertSingleGrxml,
    ConvertZipGrxml,
    Other
}

public class JobTask(MemoryStream stream, JobType jobType, Func<CancellationToken, Task>? workItem)
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public JobType Type { get; init; } = jobType;
    public byte[] Payload { get; init; } = stream.ToArray(); // Store the stream content as a byte array. Needed for serialization.
    public bool IsReady { get; set; }
    [JsonIgnore]
    public Func<CancellationToken, Task>? Worker { get; set; } = workItem;
}
