namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;

public interface IChatService
{
    Task<string> CompleteAsync(IEnumerable<ChatTurn> turns, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(IEnumerable<ChatTurn> turns, CancellationToken ct = default);
}
