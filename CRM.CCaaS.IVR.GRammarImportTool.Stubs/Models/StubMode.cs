using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StubMode
{
    Normal = 0,
    ErrorResponse = 1,
    RefuseConnection = 2,
    BadResultsAll = 3,
    BadResultsSome = 4
}

public sealed class StubBehaviorOptions
{
    public const string SectionName = "GptStub:Behavior";

    [Required]
    public StubMode Mode { get; set; } = StubMode.Normal;

    [Range(400, 599)]
    public int ErrorStatusCode { get; set; } = 500;

    public string? ErrorMessage { get; set; } = "Stub configured to return error.";
    [Range(0, 100)]
    public int BadItemsPercentage { get; set; } = 25;

    [Range(0, int.MaxValue)]
    public int MaxCorruptionsPerArray { get; set; } = 3;

    // If true and Mode == BadResultsAll, returns deliberately malformed JSON
    public bool ProduceMalformedJson { get; set; }

    public string[]? TargetProperties { get; set; }

    [Required]
    public string AdminApiKey { get; set; } = "change-me-in-dev";
}