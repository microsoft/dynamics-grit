using Microsoft.AspNetCore.SignalR;

namespace CRM.CCaaS.IVR.GRammarImportTool.ApiService.Hubs
{
    public class GritHub : Hub
    {
        // Optionally, override OnConnectedAsync to send the connectionId to the client
        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("ConnectionId", Context.ConnectionId);
            await base.OnConnectedAsync();
        }
    }
}
