using Microsoft.AspNetCore.SignalR;

namespace THYNK.Hubs
{
    public class AdminHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        public async Task SendDashboardStats(int pendingLGUCount, int pendingReportsCount, int pendingPostsCount)
        {
            await Clients.All.SendAsync("ReceiveDashboardStats", pendingLGUCount, pendingReportsCount, pendingPostsCount);
        }

        public async Task UpdateLGUApplication(string userId, bool isApproved)
        {
            await Clients.All.SendAsync("LGUApplicationUpdated", userId, isApproved);
        }

        public async Task UpdateReport(int reportId, string newStatus)
        {
            await Clients.All.SendAsync("ReportUpdated", reportId, newStatus);
        }

        public async Task UpdatePost(int postId, bool isApproved)
        {
            await Clients.All.SendAsync("PostUpdated", postId, isApproved);
        }
    }
} 