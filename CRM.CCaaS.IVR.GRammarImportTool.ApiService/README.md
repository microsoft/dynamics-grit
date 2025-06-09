# SignalR WebSocket Integration for /grit Endpoint

## C# Client Example for grithub

1. Install the NuGet package:  
   `dotnet add package Microsoft.AspNetCore.SignalR.Client`

2. Use the following C# code to connect to the hub, receive the connectionId, and listen for events:

```
using System;
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

        Console.ReadLine();
    }
}
```

Replace `<your-api-base-url>` with your actual API base URL.

---
- The client connects to `/grithub`, receives a `connectionId`, uploads the file and `connectionId` to `/grit`, and listens for progress and completion events.

