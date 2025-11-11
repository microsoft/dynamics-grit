namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Infrastructure.OpenAIChat;

public record ChatTurn
{
    public string Role { get; init; } = null!; // "system" | "user" | "assistant"
    public string Content { get; init; } = null!;

    public ChatTurn(string role, string content)
    {
        Role = role;
        Content = content;
    }
}
