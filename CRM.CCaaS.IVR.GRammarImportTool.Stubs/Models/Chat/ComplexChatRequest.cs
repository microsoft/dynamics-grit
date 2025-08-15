namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models.Chat;

public class ComplexChatRequest
{
    public List<ChatMessage> Messages { get; set; } = new();
    public string? Model { get; set; } = "gpt-4";
}
