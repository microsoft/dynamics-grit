namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models.Chat;

public record ChatMessage
{
    private string _role = "user";

    public string Role
    {
        get => _role;
        init
        {
            _role = value is "user" or "assistant" or "system"
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), "Role must be 'user', 'assistant', or 'system'.");
        }
    }

    public string Content { get; init; } = string.Empty;
}
