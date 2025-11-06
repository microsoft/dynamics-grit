using System.ComponentModel.DataAnnotations;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;
using Microsoft.Extensions.AI;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Domain.Configuration;

public class GptChatGrxmlConfiguration
{
    public const string SectionName = "GptChat:Grxml";
    public const int OneMbInBytes = 1024 * 1024;
    public const int OneHourInSeconds = 3600;
    public const string OpenAIProvider_OpenAI = "OpenAI";
    public const string OpenAIProvider_AzureOpenAI = "AzureOpenAI";

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

    [Required(ErrorMessage = "Provider selection is required")]
    public string OpenAI_Provider { get; set; } = OpenAIProvider_AzureOpenAI; //OpenAI

    //Azure OpenAI settings
    [RequireWhenAzureOpenAI(ErrorMessage = "Azure OpenAI endpoint is required")]
    public string AzureOpenAIEndpoint { get; set; } = string.Empty;

    [RequireWhenAzureOpenAI(ErrorMessage = "Azure OpenAI deployment name is required")]
    public string AzureOpenAIDeploymentName { get; set; } = string.Empty;

    [RequireWhenAzureOpenAI(ErrorMessage = "Azure OpenAI key is required")]
    public string AzureOpenAIKey { get; set; } = string.Empty;

    //Public ChatGPT OpenAI settings 
    [RequireWhenOpenAI]
    public string OpenAI_ApiKey { get; set; } = string.Empty;
    [RequireWhenOpenAI]
    public string OpenAI_Model { get; set; } = "gpt-4o";

    [Range(1, 1000, ErrorMessage = "Background tasks queue capacity")]
    public int BackgroundTasksQueueCapacity { get; set; } = 100;

    [Range(1, 60, ErrorMessage = "Maximum wait time for background task to be added in seconds")]
    public int AddBackgroundTaskMaxWaitTimeSec { get; set; } = 10;

    [Range(1, 1000, ErrorMessage = "Max items to store in InMemory conversation results store")]
    public int InMemoryConversionResultsStoreMaxItems { get; set; } = 100;

    [Required(ErrorMessage = "Path for File based conversation results store")]
    public string FileConversationResultsStorePath { get; set; } = "/tmp";

    [Range(10, 1000, ErrorMessage = "Max items to store in File conversation results store")]
    public int FileConversationResultsStoreMaxItems { get; set; } = 100;

    //AI Disclaimer
    public string? DisclaimerAI { get; set; } = string.Empty;

    // New properties for zip entry limits
    [Range(1, long.MaxValue, ErrorMessage = "Max entry size must be greater than 0")]
    public long MaxEntrySize { get; set; } = 1 * 1024 * 1024; // 1 MB

    [Range(1, long.MaxValue, ErrorMessage = "Max total uncompressed size must be greater than 0")]
    public long MaxTotalUncompressedSize { get; set; } = 100 * 1024 * 1024; // 100 MB

    [Range(1, 10000, ErrorMessage = "Max entry count must be between 1 and 10000")]
    public int MaxEntryCount { get; set; } = 1000;

    public bool HashFileNameInLogs { get; set; } = true;

    [Range(1, 100000, ErrorMessage = "Max token limit must be between 1 and 100000")]
    public int MaxTokenLimit { get; set; } = 10000;

    public string JobResultsStoreInterface { get; set; } = InMemoryConversionResultsStore.SERVICE_KEY;
}

public sealed record class GPTMessage(
    [property: Required(ErrorMessage = "Role is required")] string Role,
    [property: Required(ErrorMessage = "Content is required")] string Content
);
