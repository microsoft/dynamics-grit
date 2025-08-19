namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.Store;

public interface IConversionResultsStore
{
    Task AddResultAsync(string jobId, ConversionResult conversationResult);
    Task AppendResultAsync(string jobId, ConversionResult conversationResult);
    Task UpdateResultAsync(string jobId, ConversionResult conversationResult);
    Task<ConversionResult?> GetResultAsync(string jobId);
    Task<bool> ResultExistsAsync(string jobId);
    Task RemoveResultAsync(string jobId);
}
