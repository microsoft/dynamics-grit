using Microsoft.AspNetCore.SignalR;
using CRM.CCaaS.IVR.GRammarImportTool.ApiService.Utilities;
using System.IO;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs;

public class GritHub(GPTPrompter gptPrompter) : Hub
{
    private readonly GPTPrompter _gptPrompter = gptPrompter;

    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"Client connected: {Context.ConnectionId}");
        await Clients.Caller.SendAsync("ConnectionId", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Receives a zip file as a byte array from the client, processes it, and sends progress and result events back.
    /// </summary>
    /// <param name="zipBytes">The zip file as a byte array.</param>
    public async Task UploadZipFile(byte[] zipBytes)
    {
        Console.WriteLine($"UploadZipFile called by {Context.ConnectionId}, bytes: {zipBytes?.Length}");
        if (zipBytes == null)
        {
            await Clients.Caller.SendAsync("Error", "Uploaded file is null.");
            return;
        }

        try
        {
            using var zipStream = new MemoryStream(zipBytes);

            await _gptPrompter.GetFileEntityTypeZipAsync(
                zipStream,
                async (progress, message) =>
                {
                    await Clients.Caller.SendAsync("Progress", progress, message);
                },
                async (resultBytes) =>
                {
                    var resultBase64 = Convert.ToBase64String(resultBytes);
                    await Clients.Caller.SendAsync("Completed", resultBase64);
                }
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in UploadZipFile: {ex}");
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }
}
