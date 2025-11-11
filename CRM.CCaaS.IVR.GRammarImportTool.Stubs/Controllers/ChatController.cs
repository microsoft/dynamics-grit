using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models.Chat;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;
using Microsoft.AspNetCore.Mvc;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Controllers;

/// <summary>
/// Controller for handling chat completion requests in the stub environment.
/// Accepts chat messages, logs incoming requests, and streams simulated responses
/// to the client using the text/event-stream format.
/// </summary>
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

            foreach (var (message, index) in request.Messages.Select((msg, idx) => (msg, idx)))
            {
                _logger.LogInformation("Received request {Index} with message: {Message}", index, message.Content);
                if (index == request.Messages.Count - 1)
                {
                    if (!IsValidXml(message.Content))
                    {
                        _logger.LogWarning("Invalid XML in message: {Message}", message.Content);
                        foreach (var chunk in _chatGptService.StreamChatAsyncStub(ChatData.YAML_ERROR_DATA))
                        {
                            await Response.WriteAsync($"data: {chunk}\n\n");
                            await Response.Body.FlushAsync();
                            await Task.Delay(100); // Simulate streaming
                        }
                        return;
                    }
                }
            }

            if (Program.GetPendingError() is KeyValuePair<string, string> pendingError)
            {
                _logger.LogWarning("Sending pending error for request: {ErrorKey}", pendingError.Key);
                var baseErrorKey = GetErrorType(pendingError.Key);
                switch (baseErrorKey)
                {
                    case "timeout":
                        await Task.Delay(15000); // Simulate timeout
                        break;
                    case "disconnect":
                        Response.Body.Close(); // Simulate disconnection
                        return;
                    case "401":
                        Response.StatusCode = 401; // Unauthorized
                        break;
                    case "404":
                        Response.StatusCode = 404;
                        break;
                    case "429":
                        Response.StatusCode = 429;
                        break;
                    case "corrupted-json":
                        await Response.WriteAsync("Invalid Json");
                        return;
                    default:
                        break;
                }
                await Response.WriteAsync($"data: Error: {pendingError.Value}\n\n");
                await Response.Body.FlushAsync();
                return;
            }
            // Simulate streaming a valid response
            foreach (var chunk in _chatGptService.StreamChatAsyncStub(ChatData.YAML_REPLY_DATA[Random.Shared.Next(ChatData.YAML_REPLY_DATA.Length)]))
            {
                await Response.WriteAsync($"data: {chunk}\n\n");
                await Response.Body.FlushAsync();
                await Task.Delay(100); // Simulate streaming
            }
        }
    }
    private bool IsValidXml(string xml)
    {
        foreach (string prompt in ChatData.GRXML_PROMPTS)
        {
            xml = Regex.Replace(xml, prompt, string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        try
        {
            using var stringReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(stringReader);
            while (xmlReader.Read()) { }
            return true;
        }
        catch (XmlException)
        {
            _logger.LogWarning("Invalid XML format detected.");
            return false;
        }
    }
    public static string GetErrorType(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        var withoutIndex = key.Split(Program.PendingErrorSeparator)[0];
        return withoutIndex;
    }
}
