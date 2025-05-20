using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using THYNK.Data;
using THYNK.Hubs;
using THYNK.Models;
using TimeZoneConverter;

namespace THYNK.Controllers.API
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<CommunityHub> _communityHubContext;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            ApplicationDbContext context, 
            IHubContext<CommunityHub> communityHubContext,
            ILogger<NotificationsController> logger)
        {
            _context = context;
            _communityHubContext = communityHubContext;
            _logger = logger;
        }

        private DateTime GetPhilippineTime()
        {
            var phTimeZone = TZConvert.GetTimeZoneInfo("Asia/Manila");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);
        }

        [HttpGet("GetRecent")]
        public async Task<ActionResult<IEnumerable<UserNotification>>> GetRecent()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized();
                }

                var notifications = await _context.UserNotifications
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(15) // Limit to 15 most recent notifications
                    .ToListAsync();
                
                _logger.LogInformation($"Retrieved {notifications.Count} notifications for user {userId}");
                return Ok(notifications);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving notifications");
                return StatusCode(500, new { error = "An error occurred while retrieving notifications" });
            }
        }

        [HttpGet("UnreadCount")]
        public async Task<ActionResult<int>> GetUnreadCount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var count = await _context.UserNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);
                
            return count;
        }
        
        [HttpPost("MarkAsRead/{id}")]
        public async Task<ActionResult<object>> MarkAsRead(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }
            
            try
            {
                // Find the notification
                var notification = await _context.UserNotifications
                    .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
                
                if (notification == null)
                {
                    return NotFound(new { success = false, message = "Notification not found" });
                }
                
                // Mark as read
                notification.IsRead = true;
                await _context.SaveChangesAsync();
                
                // Get updated count
                var unreadCount = await _context.UserNotifications
                    .CountAsync(n => n.UserId == userId && !n.IsRead);
                
                return Ok(new { 
                    success = true, 
                    message = "Notification marked as read", 
                    unreadCount 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification as read");
                return StatusCode(500, new { success = false, message = "An error occurred" });
            }
        }
        
        [HttpPost("MarkAllAsRead")]
        public async Task<ActionResult<object>> MarkAllAsRead()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }
            
            try
            {
                // Get all unread notifications
                var notifications = await _context.UserNotifications
                    .Where(n => n.UserId == userId && !n.IsRead)
                    .ToListAsync();
                
                // Mark all as read
                foreach (var notification in notifications)
                {
                    notification.IsRead = true;
                }
                
                await _context.SaveChangesAsync();
                
                return Ok(new { 
                    success = true, 
                    message = $"Marked {notifications.Count} notifications as read", 
                    unreadCount = 0 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read");
                return StatusCode(500, new { success = false, message = "An error occurred" });
            }
        }
        
        // Add a notification for a user
        [HttpPost("Add")]
        [Authorize(Roles = "Admin,LGU")]
        public async Task<ActionResult<object>> AddNotification([FromBody] AddNotificationRequest request)
        {
            if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.Message))
            {
                return BadRequest(new { success = false, message = "UserId, Title and Message are required" });
            }
            
            try
            {
                // Create notification
                var notification = new UserNotification
                {
                    UserId = request.UserId,
                    Title = request.Title,
                    Message = request.Message,
                    NotificationType = request.NotificationType ?? "info",
                    RelatedEntityId = request.RelatedEntityId,
                    RelatedEntityType = request.RelatedEntityType,
                    IsRead = false,
                    CreatedAt = GetPhilippineTime()
                };
                
                // Add to database
                _context.UserNotifications.Add(notification);
                await _context.SaveChangesAsync();
                
                // Send real-time notification
                await _communityHubContext.Clients.Group($"user_{request.UserId}")
                    .SendAsync("NotificationReceived", new {
                        id = notification.Id,
                        title = notification.Title,
                        message = notification.Message,
                        type = notification.NotificationType,
                        createdAt = notification.CreatedAt // No need to convert since it's already in PH time
                    });
                
                // Get updated count
                var unreadCount = await _context.UserNotifications
                    .CountAsync(n => n.UserId == request.UserId && !n.IsRead);
                
                // Update badge count
                await _communityHubContext.Clients.Group($"user_{request.UserId}")
                    .SendAsync("UpdateNotificationBadge", unreadCount);
                
                return Ok(new { 
                    success = true, 
                    message = "Notification sent", 
                    notificationId = notification.Id 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification");
                return StatusCode(500, new { success = false, message = "An error occurred" });
            }
        }
    }
    
    public class AddNotificationRequest
    {
        public string UserId { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string? NotificationType { get; set; }
        public int? RelatedEntityId { get; set; }
        public string? RelatedEntityType { get; set; }
    }
} 