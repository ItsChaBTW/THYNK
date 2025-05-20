using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using THYNK.Models;

namespace THYNK.Hubs
{
    public class AdminHub : Hub
    {
        private readonly ILogger<AdminHub> _logger;

        public AdminHub(ILogger<AdminHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Admin client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public async Task JoinUserGroup(string userId)
        {
            _logger.LogInformation($"Admin user {userId} joining group: user_{userId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            _logger.LogInformation($"Admin user {userId} successfully joined group");
            
            // Send confirmation to the client with correct method name
            await Clients.Caller.SendAsync("JoinedGroup", userId);
        }

        public async Task SendDashboardStats(int pendingLGUCount, int pendingReportsCount, int pendingPostsCount)
        {
            _logger.LogInformation($"Sending dashboard stats: LGU={pendingLGUCount}, Reports={pendingReportsCount}, Posts={pendingPostsCount}");
            await Clients.All.SendAsync("ReceiveDashboardStats", pendingLGUCount, pendingReportsCount, pendingPostsCount);
        }

        public async Task UpdateLGUApplication(string userId, bool isApproved)
        {
            _logger.LogInformation($"LGU application update: User={userId}, Approved={isApproved}");
            await Clients.All.SendAsync("LGUApplicationUpdated", userId, isApproved);
        }

        public async Task UpdateReport(int reportId, string newStatus)
        {
            _logger.LogInformation($"Report update: ID={reportId}, Status={newStatus}");
            await Clients.All.SendAsync("ReportUpdated", reportId, newStatus);
        }

        public async Task UpdatePost(int postId, bool isApproved)
        {
            _logger.LogInformation($"Post update: ID={postId}, Approved={isApproved}");
            await Clients.All.SendAsync("PostUpdated", postId, isApproved);
        }

        public async Task NotifyNewIncidentReport(DisasterReport report)
        {
            _logger.LogInformation($"New incident report notification: ID={report.Id}, Title={report.Title}");
            await Clients.All.SendAsync("NewIncidentReport", report);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _logger.LogInformation($"Admin client disconnected: {Context.ConnectionId}");
            if (exception != null)
            {
                _logger.LogError($"Admin disconnection error: {exception.Message}");
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
} 