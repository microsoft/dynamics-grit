# SignalR WebSocket Integration for /grit Endpoint

## C# Client Example for grithub

1. Install the NuGet package:  
   `dotnet add package Microsoft.AspNetCore.SignalR.Client`

2. Use the following C# code to connect to the hub, send a zip file as a byte array using `UploadZipFile`, and listen for events:
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

class Program
{
    static async Task Main(string[] args)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("<your-api-base-url>/grithub")
            .Build();
        connection.On<string>("ConnectionId", connectionId =>
        {
            Console.WriteLine($"ConnectionId received: {connectionId}");
        });

        connection.On<int>("Progress", progress =>
        {
            Console.WriteLine($"Progress: {progress}%");
        });

        connection.On("Completed", () =>
        {
            Console.WriteLine("Upload completed.");
        });

        connection.On<string>("Error", error =>
        {
            Console.WriteLine($"Error: {error}");
        });

        await connection.StartAsync();
        Console.WriteLine("Connection started.");

        // Replace <your-api-base-url> and zipFilePath with your actual values
        string zipFilePath = "<path-to-your-zip-file>";
        byte[] zipFileBytes = File.ReadAllBytes(zipFilePath);
        await connection.InvokeAsync("UploadZipFile", zipFileBytes);
        Console.WriteLine("File upload initiated.");

        Console.ReadLine();
    }
}
Replace `<your-api-base-url>` and `zipFilePath` with your actual values.

---
- The client connects to `/grithub`, receives a `connectionId`, uploads the file using `UploadZipFile` (as a byte array), and listens for progress and completion events.

## Troubleshooting SignalR Connection Errors

- Ensure the client connects to the correct hub endpoint (e.g., `/grithub`) and starts the connection before calling `UploadZipFile`.
- The method name and parameter type must match: `UploadZipFile(byte[] zipBytes)`.
- CORS must be enabled on the server to allow WebSocket/SignalR traffic from your client origin.
- Check server logs for connection attempts and errors.
- If using authentication, ensure credentials/tokens are provided.
- Add logging to both client and server for connection and method invocation events.

