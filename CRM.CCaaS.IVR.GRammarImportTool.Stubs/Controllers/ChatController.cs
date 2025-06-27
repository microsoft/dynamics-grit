using System.Text.Json;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models.Chat;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;
using Microsoft.AspNetCore.Mvc;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Controllers;
[ApiController]
[Route("openai/deployments/test/[controller]/completions")]
public class ChatController(ChatGptService chatGptService, ILogger<ChatGptService> logger) : ControllerBase
{
    private readonly ChatGptService _chatGptService = chatGptService;
    private readonly ILogger<ChatGptService> _logger = logger;

    [HttpPost]
    public async Task Post([FromBody] ComplexChatRequest request)
    {
        {
            Response.ContentType = "text/event-stream";

            if (request == null)
            {
                await Response.WriteAsync("data: Error: Request can't be null\n\n");
                await Response.Body.FlushAsync();
                return;
            }

            foreach (var message in request.Messages)
            {
                _logger.LogInformation("Received request with message: {Message}", message.Content);
            }

            foreach (var chunk in _chatGptService.StreamChatAsyncStub(ChatData.YAML_REPLY_DATA[Random.Shared.Next(ChatData.YAML_REPLY_DATA.Length)]))
            {
                await Response.WriteAsync($"data: {chunk}\n\n");
                await Response.Body.FlushAsync();
                await Task.Delay(100); // Simulate streaming
            }
        }
    }
}
