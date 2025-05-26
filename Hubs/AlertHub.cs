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
            // Create a broadcast-safe version of the alert
            var broadcastAlert = new
            {
                alert.Id,
                alert.Title,
                alert.Message,
                alert.Severity,
                alert.DateIssued,
                alert.ExpiresAt,
                alert.IsActive,
                alert.AffectedArea,
                alert.ImagePath,
                alert.BackgroundStyle,
                alert.IconStyle,
                alert.ColorScheme,
                issuedBy = alert.User is LGUUser lguUser ? lguUser.OrganizationName : "LGU",
                User = new
                {
                    Id = alert.User?.Id,
                    Name = alert.User != null
                        ? $"{alert.User.FirstName} {alert.User.LastName}"
                        : "System"
                }
            };

            await Clients.All.SendAsync("ReceiveAlert", broadcastAlert);
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