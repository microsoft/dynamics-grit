using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;

public class GptChatGrxmlConfiguration
{
    public const string SectionName = "GptChat:Grxml";

    [Range(1, 100, ErrorMessage = "Degree of parallelism for processing")]
    public int DegreeParallelism { get; set; } = 7; // Degree of parallelism for processing

    [Range(1, 10, ErrorMessage = "Maximum number of retries for processing of single file")]
    public int MaxRetries { get; set; } = 3; // Maximum number of retries for processing

    [Range(1, 300, ErrorMessage = "Retry delay in seconds")]
    public int RetryDelaySec { get; set; } = 10; // Delay between retries in seconds

    [Range(1, 120, ErrorMessage = "Maximum allowed conversion time in minutes")]
    public int MaxAllowedConversionTimeMinutes { get; set; } = 60;

    [Range(10, 20 * 1025 * 1024, ErrorMessage = "Allowed upload file size range in bytes")]
    public int AllowedUploadFileSizeRangeBytes { get; set; } = 10 + 1024 * 1024;

    [Range(10, 20 * 1025 * 1024, ErrorMessage = "Max SignalR hub message size in bytes")]
    public int MaxSignalRMessageSizeBytes { get; set; } = 5 + 1024 * 1024;

    [Range(1, 1000, ErrorMessage = "Channel depth for result stream")]
    public int ResultStreamChannelCapacity { get; set; } = 100;

    [Required(ErrorMessage = "Initial Chat history is required")]
    public List<GPTMessage>? InitialChatHistory { get; set; } = new List<GPTMessage>();

    [Required(ErrorMessage = "Azure OpenAI endpoint is required")]
    public string AzureOpenAIEndpoint { get; set; } = string.Empty;

    [Required(ErrorMessage = "Azure OpenAI deployment name is required")]
    public string AzureOpenAIDeploymentName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Azure OpenAI key is required")]
    public string AzureOpenAIKey { get; set; } = string.Empty;

    //AI Disclaimer
    public string? DisclaimerAI { get; set; } = string.Empty;
}

public class GPTMessage
{
    [Required(ErrorMessage = "Role is required")]
    public string Role { get; set; } = string.Empty;
    [Required(ErrorMessage = "Content is required")]
    public string Content { get; set; } = string.Empty;
}
