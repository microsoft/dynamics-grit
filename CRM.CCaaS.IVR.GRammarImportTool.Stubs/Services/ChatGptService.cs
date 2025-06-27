using System.Collections.Generic;
using System.Data;
using System.Net.Http.Headers;
using System.Text.Json;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models.Chat;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;

public class ChatGptService(IConfiguration config, ILogger<ChatGptService> logger)
{
    private readonly IConfiguration _config = config;
    private readonly ILogger _logger = logger;
    private static readonly char[] _separator = new char[] { '\n' };

    public IEnumerable<string> StreamChatAsyncStub(string yamlInput)
    {
        ArgumentException.ThrowIfNullOrEmpty(yamlInput, nameof(yamlInput));
        var listNodes = yamlInput.Split(_separator);

        yield return /*lang=json,strict*/ "{\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}"; //header

        foreach (var word in listNodes)
        {
            var chunk = new
            {
                choices = new[]
                {
                    new
                    {
                        delta = new { content = word }
                    }
                }
            };

            yield return $"{JsonSerializer.Serialize(chunk)}\n";
        }

        yield return "[DONE]\n"; //footer
    }
}
