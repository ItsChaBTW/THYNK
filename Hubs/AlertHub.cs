using Microsoft.AspNetCore.SignalR;
using THYNK.Models;

namespace THYNK.Hubs
{
    public class AlertHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        // Method to broadcast a new alert to all connected clients
        public async Task BroadcastAlert(Alert alert)
        {
            await Clients.All.SendAsync("ReceiveAlert", alert);
        }

        // Method to broadcast an alert update to all connected clients
        public async Task UpdateAlert(Alert alert)
        {
            await Clients.All.SendAsync("AlertUpdated", alert);
        }

        // Method to broadcast alert deletion to all connected clients
        public async Task DeleteAlert(int alertId)
        {
            await Clients.All.SendAsync("AlertDeleted", alertId);
        }
        
        // Method for clients to verify connection is working properly
        public async Task PingConnection()
        {
            await Clients.Caller.SendAsync("ConnectionVerified", new { 
                status = "connected", 
                timestamp = DateTime.Now.ToString("o"),
                connectionId = Context.ConnectionId
            });
        }
    }
} 