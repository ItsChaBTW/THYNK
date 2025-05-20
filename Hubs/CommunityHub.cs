using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using THYNK.Models;
using System;

namespace THYNK.Hubs
{
    public class CommunityHub : Hub
    {
        private readonly ILogger<CommunityHub> _logger;

        public CommunityHub(ILogger<CommunityHub> logger)
        {
            _logger = logger;
        }

        public async Task JoinUserGroup(string userId)
        {
            _logger.LogInformation($"User {userId} joining group: user_{userId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            _logger.LogInformation($"User {userId} successfully joined group");
            
            // Send confirmation to the client
            await Clients.Caller.SendAsync("JoinedGroup", userId);
        }

        public async Task LeaveUserGroup(string userId)
        {
            _logger.LogInformation($"User {userId} leaving group: user_{userId}");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        public async Task NotifyNewReportAssignment(string userId, DisasterReport report)
        {
            _logger.LogInformation($"Notifying user {userId} about new report assignment: {report.Id}");
            await Clients.Group($"user_{userId}").SendAsync("NewReportAssigned", report);
        }

        public async Task NotifyPostModeration(string userId, int postId, bool isApproved, string reason = null)
        {
            _logger.LogInformation($"Notifying user {userId} about post moderation: {postId}, Approved: {isApproved}");
            await Clients.Group($"user_{userId}").SendAsync("PostModerationUpdate", new {
                postId = postId,
                isApproved = isApproved,
                reason = reason,
                timestamp = DateTime.Now
            });
        }

        public async Task UpdateReportStatus(string userId, int reportId, string status, string title)
        {
            _logger.LogInformation($"Sending report status update to user {userId}: Report {reportId} changed to {status}");
            await Clients.Group($"user_{userId}").SendAsync("ReportStatusUpdated", reportId, status, title);
            await Clients.Caller.SendAsync("ReportStatusUpdateSent", userId, reportId, status);
        }
        
        public async Task SendNotification(string userId, int notificationId, string title, string message, string type)
        {
            _logger.LogInformation($"Sending notification to user {userId}: {title}");
            await Clients.Group($"user_{userId}").SendAsync("NewNotification", notificationId, title, message, type, System.DateTime.Now.ToString("MMM dd, yyyy HH:mm"));
            await Clients.Caller.SendAsync("NotificationSent", userId, notificationId);
            
            // Also send a direct notification received event that is more reliable
            await Clients.Group($"user_{userId}").SendAsync("NotificationReceived", 1); // Increment by 1
        }
        
        public async Task MarkNotificationAsRead(string userId, int notificationId)
        {
            _logger.LogInformation($"Marking notification {notificationId} as read for user {userId}");
            await Clients.Group($"user_{userId}").SendAsync("NotificationRead", notificationId);
        }
        
        public async Task UpdateNotificationBadge(string userId, int count)
        {
            _logger.LogInformation($"Updating notification badge for user {userId} to {count}");
            await Clients.Group($"user_{userId}").SendAsync("UpdateNotificationBadge", count);
            
            // Also send the more reliable direct notification event
            await Clients.Group($"user_{userId}").SendAsync("NotificationReceived", count);
        }
        
        public async Task DirectNotify(string userId, int count)
        {
            _logger.LogInformation($"Sending direct notification to user {userId} with count {count}");
            await Clients.Group($"user_{userId}").SendAsync("NotificationReceived", count);
        }
        
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }
        
        public override async Task OnDisconnectedAsync(System.Exception exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            if (exception != null)
            {
                _logger.LogError($"Disconnection error: {exception.Message}");
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
} 