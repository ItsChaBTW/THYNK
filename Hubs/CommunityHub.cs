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
            try
            {
                _logger.LogInformation($"User {userId} joining group: user_{userId}");
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.LogInformation($"User {userId} successfully joined group");
                
                // Send confirmation to the client with correct case
                await Clients.Caller.SendAsync("JoinedGroup", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error joining user group: {ex.Message}");
                throw;
            }
        }

        public async Task LeaveUserGroup(string userId)
        {
            try
            {
                _logger.LogInformation($"User {userId} leaving group: user_{userId}");
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.LogInformation($"User {userId} successfully left group");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error leaving user group: {ex.Message}");
                throw;
            }
        }

        public async Task NotifyNewReportAssignment(string userId, DisasterReport report)
        {
            try
            {
                _logger.LogInformation($"Notifying user {userId} about new report assignment: {report.Id}");
                await Clients.Group($"user_{userId}").SendAsync("NewReportAssigned", report);
                _logger.LogInformation($"Successfully sent report assignment notification to user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending report assignment notification: {ex.Message}");
                throw;
            }
        }

        public async Task NotifyPostModeration(string userId, int postId, bool isApproved, string reason = null)
        {
            try
            {
                _logger.LogInformation($"Notifying user {userId} about post moderation: {postId}, Approved: {isApproved}");
                await Clients.Group($"user_{userId}").SendAsync("PostModerationUpdate", new {
                    postId = postId,
                    isApproved = isApproved,
                    reason = reason,
                    timestamp = DateTime.Now
                });
                _logger.LogInformation($"Successfully sent post moderation notification to user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending post moderation notification: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateReportStatus(string userId, int reportId, string status, string title)
        {
            try
            {
                _logger.LogInformation($"Sending report status update to user {userId}: Report {reportId} changed to {status}");
                
                // Create notification data
                var notificationData = new { 
                    reportId = reportId,
                    status = status,
                    title = title,
                    timestamp = DateTime.Now
                };

                // Send to specific user group
                await Clients.Group($"user_{userId}").SendAsync("ReportStatusUpdated", reportId, status, title);
                
                // Send to all clients to ensure updates are received
                await Clients.All.SendAsync("ReportStatusUpdated", reportId, status, title);
                
                // Send notification received event to trigger UI update
                await Clients.Group($"user_{userId}").SendAsync("NotificationReceived", notificationData);
                
                // Also send to all clients to ensure updates are received
                await Clients.All.SendAsync("NotificationReceived", notificationData);

                // Force reload notifications for all clients
                await Clients.All.SendAsync("ForceReloadNotifications");
                
                _logger.LogInformation($"Successfully sent report status update to user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending report status update: {ex.Message}");
                throw;
            }
        }
        
        public async Task SendNotification(string userId, int notificationId, string title, string message, string type)
        {
            try
            {
                _logger.LogInformation($"Sending notification to user {userId}: {title}");
                
                // Send to specific user group
                await Clients.Group($"user_{userId}").SendAsync("NewNotification", notificationId, title, message, type, DateTime.Now.ToString("MMM dd, yyyy HH:mm"));
                
                // Also send to all clients to ensure updates are received
                await Clients.All.SendAsync("NewNotification", notificationId, title, message, type, DateTime.Now.ToString("MMM dd, yyyy HH:mm"));
                
                // Send notification received event to trigger UI update
                await Clients.Group($"user_{userId}").SendAsync("NotificationReceived", new { 
                    notificationId = notificationId,
                    title = title,
                    message = message,
                    type = type,
                    timestamp = DateTime.Now
                });
                
                _logger.LogInformation($"Successfully sent notification to user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending notification: {ex.Message}");
                throw;
            }
        }
        
        public async Task MarkNotificationAsRead(string userId, int notificationId)
        {
            try
            {
                _logger.LogInformation($"Marking notification {notificationId} as read for user {userId}");
                await Clients.Group($"user_{userId}").SendAsync("NotificationRead", notificationId);
                _logger.LogInformation($"Successfully marked notification {notificationId} as read");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error marking notification as read: {ex.Message}");
                throw;
            }
        }
        
        public async Task UpdateNotificationBadge(string userId, int count)
        {
            try
            {
                _logger.LogInformation($"Updating notification badge for user {userId} to {count}");
                await Clients.Group($"user_{userId}").SendAsync("UpdateNotificationBadge", count);
                _logger.LogInformation($"Successfully updated notification badge for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating notification badge: {ex.Message}");
                throw;
            }
        }
        
        public async Task DirectNotify(string userId, int count)
        {
            try
            {
                _logger.LogInformation($"Sending direct notification to user {userId} with count {count}");
                await Clients.Group($"user_{userId}").SendAsync("NotificationReceived", count);
                _logger.LogInformation($"Successfully sent direct notification to user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending direct notification: {ex.Message}");
                throw;
            }
        }
        
        public override async Task OnConnectedAsync()
        {
            try
            {
                _logger.LogInformation($"Client connected: {Context.ConnectionId}");
                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in OnConnectedAsync: {ex.Message}");
                throw;
            }
        }
        
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            try
            {
                _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
                if (exception != null)
                {
                    _logger.LogError($"Disconnection error: {exception.Message}");
                }
                await base.OnDisconnectedAsync(exception);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in OnDisconnectedAsync: {ex.Message}");
                throw;
            }
        }
    }
} 