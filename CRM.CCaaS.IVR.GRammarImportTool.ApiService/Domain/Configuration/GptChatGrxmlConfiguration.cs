using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;

public class GptChatGrxmlConfiguration
{
    public const string SectionName = "GptChat:Grxml";
    public const int OneMbInBytes = 1024 * 1024;
    public const int OneHourInSeconds = 3600;
    public bool _disposedValue;

    [Range(1, 100, ErrorMessage = "Degree of parallelism for processing")]
    public int DegreeParallelism { get; set; } = 7; // Degree of parallelism for processing

    [Range(1, 10, ErrorMessage = "Maximum number of retries for processing of single file")]
    public int MaxRetries { get; set; } = 3; // Maximum number of retries for processing

    [Range(1, 300, ErrorMessage = "Retry delay in seconds")]
    public int RetryDelaySec { get; set; } = 10; // Delay between retries in seconds

    [Range(1, 1200, ErrorMessage = "Maximum allowed conversion time for single file in seconds")]
    public int MaxAllowedConversionTimeSingleFileSec { get; set; } = 600;

    [Range(1, 7200, ErrorMessage = "Maximum allowed conversion time for all files in seconds")]
    public int MaxAllowedConversionTimeTotalSec { get; set; } = OneHourInSeconds;

    [Range(10, 20 * 1025 * 1024, ErrorMessage = "Allowed upload file size range in bytes")]
    public int AllowedUploadFileSizeRangeBytes { get; set; } = 10 * OneMbInBytes;

    [Range(10, 20 * 1025 * 1024, ErrorMessage = "Max SignalR hub message size in bytes")]
    public int MaxSignalRMessageSizeBytes { get; set; } = 5 * OneMbInBytes;

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

    // New properties for zip entry limits
    [Range(1, long.MaxValue, ErrorMessage = "Max entry size must be greater than 0")]
    public long MaxEntrySize { get; set; } = 1 * 1024 * 1024; // 1 MB

    [Range(1, long.MaxValue, ErrorMessage = "Max total uncompressed size must be greater than 0")]
    public long MaxTotalUncompressedSize { get; set; } = 100 * 1024 * 1024; // 100 MB

    [Range(1, 10000, ErrorMessage = "Max entry count must be between 1 and 10000")]
    public int MaxEntryCount { get; set; } = 1000;
}

public class GPTMessage
{
    [Required(ErrorMessage = "Role is required")]
    public string Role { get; set; } = string.Empty;
    [Required(ErrorMessage = "Content is required")]
    public string Content { get; set; } = string.Empty;
}
