namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models.Chat;

public class ChatMessageDto
{
    public string Role { get; set; } = "user"; // "user", "assistant", or "system"
    public string Content { get; set; } = string.Empty;
}
