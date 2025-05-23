using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using THYNK.Data;
using THYNK.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authentication;
using System.Text.Json;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Hosting;
using THYNK.Hubs;
using THYNK.Services;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace THYNK.Controllers
{
    [Authorize]
    public class LGUController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LGUController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<AdminHub> _hubContext;
        private readonly IHubContext<CommunityHub> _communityHubContext;
        private readonly IHubContext<AlertHub> _alertHubContext;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly AlertNotificationService _alertNotificationService;

        public LGUController(
            ApplicationDbContext context, 
            ILogger<LGUController> logger, 
            UserManager<ApplicationUser> userManager, 
            IHubContext<AdminHub> hubContext,
            IHubContext<CommunityHub> communityHubContext,
            IHubContext<AlertHub> alertHubContext,
            IEmailSender emailSender,
            IWebHostEnvironment webHostEnvironment,
            AlertNotificationService alertNotificationService)
        {
            _context = context;
            _logger = logger;
            _userManager = userManager;
            _hubContext = hubContext;
            _communityHubContext = communityHubContext;
            _alertHubContext = alertHubContext;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
            _alertNotificationService = alertNotificationService;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Allow ResourceLibrary and ViewResource to be accessed without authentication
            string actionName = context.RouteData.Values["action"]?.ToString();
            if (actionName == "ResourceLibrary" || actionName == "ViewResource")
            {
                await next();
                return;
            }
            
            if (!User.Identity.IsAuthenticated)
            {
                context.Result = new ChallengeResult();
                return;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                context.Result = new ForbidResult();
                return;
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || user.UserRole != UserRoleType.LGU)
            {
                _logger.LogWarning($"Access denied for user {userId}. User is null: {user == null}, UserRole: {user?.UserRole}");
                context.Result = new RedirectToActionResult("AccessDenied", "Account", new { area = "Identity" });
                return;
            }

            // Check if LGU user is approved
            if (user is LGUUser lguUser && !lguUser.IsApproved)
            {
                _logger.LogWarning($"Access denied for unapproved LGU user {userId}");
                await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Result = new RedirectToPageResult("/Account/PendingApproval", new { area = "Identity" });
                return;
            }

            await base.OnActionExecutionAsync(context, next);
        }

        // Dashboard main page
        public async Task<IActionResult> Dashboard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Get recent disaster reports assigned to this LGU organization
            var recentReports = await _context.DisasterReports
                .Where(r => r.AssignedToId == userId || 
                           (r.Status == ReportStatus.InProgress && r.AssignedTo.OrganizationName == currentUser.OrganizationName))
                .OrderByDescending(r => r.DateReported)
                .Take(5)
                .ToListAsync();
                
            // Get active alerts
            var activeAlerts = await _context.Alerts
                .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now))
                .OrderByDescending(a => a.DateIssued)
                .Take(5)
                .ToListAsync();
                
            // Get community updates
            var communityUpdates = await _context.CommunityUpdates
                .Where(c => c.ModerationStatus == ModerationStatus.Approved)
                .OrderByDescending(c => c.DatePosted)
                .Take(10)
                .ToListAsync();
                
            ViewBag.RecentReports = recentReports;
            ViewBag.ActiveAlerts = activeAlerts;
            ViewBag.CommunityUpdates = communityUpdates;
            
            return View();
        }

        // Manage disaster reports with filtering
        public async Task<IActionResult> ManageReports(string status, string search)
        {
            _logger.LogInformation($"----------- DEBUG START: ManageReports -----------");
            _logger.LogInformation($"Parameters: status={status}, search={search}");

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(currentUserId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }

            _logger.LogInformation($"Current user: {currentUser.Id}, Organization: {currentUser.OrganizationName}");

            // Get reports
            IQueryable<DisasterReport> reportsQuery = _context.DisasterReports;

            // Default behavior - only get specific reports
            reportsQuery = reportsQuery.Where(r => 
                r.AssignedToId == currentUserId || 
                (r.Status == ReportStatus.InProgress && r.AssignedTo.OrganizationName == currentUser.OrganizationName)
            );

            var reports = await reportsQuery.ToListAsync();
            _logger.LogInformation($"STEP 1: Retrieved {reports.Count} reports after initial query");
            foreach (var r in reports)
            {
                _logger.LogInformation($"Report ID: {r.Id}, Title: {r.Title}, Status: {r.Status}, Severity: {r.Severity} ({(int)r.Severity}), Date: {r.DateReported:yyyy-MM-dd HH:mm}");
            }

            // Apply status filter if specified
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var statusEnum))
            {
                reports = reports.Where(r => r.Status == statusEnum).ToList();
                ViewBag.CurrentFilter = statusEnum;
            }

            // Apply search filter if specified
            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                reports = reports.Where(r =>
                    r.Title.ToLower().Contains(search) ||
                    r.Description.ToLower().Contains(search) ||
                    r.Location.ToLower().Contains(search) ||
                    r.Type.ToString().ToLower().Contains(search)
                ).ToList();
                ViewBag.CurrentSearch = search;
            }

            // Sort reports by severity if in progress
            if (ViewBag.CurrentFilter == ReportStatus.InProgress)
            {
                reports = reports
                    .OrderBy(r => (int)r.Severity)  // Primary sort by severity
                    .ThenByDescending(r => r.DateReported)  // Secondary sort by date
                    .ToList();
            }
            else
            {
                reports = reports.OrderByDescending(r => r.DateReported).ToList();
            }

            _logger.LogInformation($"FINAL: Returning {reports.Count} reports to view");
            _logger.LogInformation("FINAL ORDER OF REPORTS:");
            int index = 1;
            foreach (var r in reports)
            {
                _logger.LogInformation($"{index++}. ID: {r.Id}, Title: {r.Title}, Status: {r.Status}, Severity: {r.Severity} ({(int)r.Severity}), Date: {r.DateReported:yyyy-MM-dd HH:mm}");
            }
            _logger.LogInformation($"----------- DEBUG END: ManageReports -----------");
            
            // Return partial view for AJAX requests
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_LGUReportsTable", reports);
            }
            
            return View(reports);
        }
        
        // View report details
        public async Task<IActionResult> ReportDetails(int? id)
        {
            if (!id.HasValue)
            {
                TempData["ErrorMessage"] = "Invalid report ID.";
                return RedirectToAction(nameof(ManageReports));
            }

            var report = await _context.DisasterReports
                .Include(r => r.User)
                .Include(r => r.AssignedTo)
                .FirstOrDefaultAsync(r => r.Id == id.Value);
                
            if (report == null)
            {
                TempData["ErrorMessage"] = "Report not found.";
                return RedirectToAction(nameof(ManageReports));
            }

            // Verify this LGU user has jurisdiction over this report
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }

            bool hasJurisdiction = report.AssignedToId == userId || 
                (report.Status == ReportStatus.InProgress && 
                 report.AssignedTo != null && 
                 report.AssignedTo.OrganizationName == currentUser.OrganizationName);

            if (!hasJurisdiction)
            {
                TempData["ErrorMessage"] = "You don't have access to this report as it's not in your jurisdiction.";
                return RedirectToAction(nameof(ManageReports));
            }
            
            return View(report);
        }
        
        // Helper method to convert report to DTO for SignalR
        private object ToRecentReportDto(DisasterReport report)
        {
            return new
            {
                Id = report.Id,
                Title = report.Title,
                Type = report.Type.ToString(),
                Barangay = report.Barangay,
                City = report.City,
                DateReported = report.DateReported.ToString("MMM dd, yyyy HH:mm"),
                Status = report.Status.ToString()
            };
        }

        // Update report status
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReportStatus(int id, int status, string notes)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);
            
            if (report == null)
            {
                return NotFound();
            }
            
            var oldStatus = report.Status;
            report.Status = (ReportStatus)status;
            
            // Add response log entry
            var currentUser = await _userManager.GetUserAsync(User) as LGUUser;
            string responderName = $"{currentUser?.FirstName ?? "Unknown"} {currentUser?.LastName ?? "User"}";
            string responseNote = $"[{DateTime.Now:yyyy-MM-dd HH:mm}] Status updated to {report.Status} by {responderName} ({currentUser?.OrganizationName}). Notes: {notes}";
            
            // Append to existing info with a separator
            if (!string.IsNullOrEmpty(report.AdditionalInfo))
            {
                report.AdditionalInfo += "\n\n---\n\n";
            }
            report.AdditionalInfo += responseNote;
            
            // Update timestamps based on status
            switch (report.Status)
            {
                case ReportStatus.InProgress:
                    report.DateInProgress = DateTime.Now;
                    break;
                case ReportStatus.Resolved:
                    report.ResolvedAt = DateTime.Now;
                    break;
            }
            
            _context.Update(report);
            await _context.SaveChangesAsync();
            
            // Create notification for the report owner
            if (report.User != null)
            {
                string notificationTitle = "";
                string notificationMessage = "";
                string notificationType = "info";

                switch (report.Status)
                {
                    case ReportStatus.InProgress:
                        notificationTitle = "Report In Progress";
                        notificationMessage = $"Your report '{report.Title}' is now being addressed by {currentUser?.OrganizationName}.";
                        notificationType = "info";
                        break;
                    case ReportStatus.Resolved:
                        notificationTitle = "Report Resolved";
                        notificationMessage = $"Your report '{report.Title}' has been resolved by {currentUser?.OrganizationName}.";
                        notificationType = "success";
                        break;
                }

                var notification = new UserNotification
                {
                    UserId = report.UserId,
                    Title = notificationTitle,
                    Message = notificationMessage,
                    NotificationType = notificationType,
                    RelatedEntityId = report.Id,
                    RelatedEntityType = "Report",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                };

                _context.UserNotifications.Add(notification);
                await _context.SaveChangesAsync();

                // Send real-time notification to report owner
                await _hubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("NotificationReceived", notification);
                await _hubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("ReportStatusUpdated", report.Id, report.Status.ToString(), report.Title);

                // Also send through community hub
                await _communityHubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("NotificationReceived", notification);
                await _communityHubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("ReportStatusUpdated", report.Id, report.Status.ToString(), report.Title);

                // Force reload notifications for all clients
                await _hubContext.Clients.All.SendAsync("ForceReloadNotifications");
                await _communityHubContext.Clients.All.SendAsync("ForceReloadNotifications");

                // Get notification preferences
                var notificationPreferences = await _context.NotificationPreferences
                    .FirstOrDefaultAsync(np => np.UserId == report.UserId);

                // Send email notification if enabled
                if (notificationPreferences?.EmailEnabled == true && !string.IsNullOrEmpty(report.User.Email))
                {
                    try
                    {
                        await _emailSender.SendEmailAsync(
                            report.User.Email,
                            $"THYNK - Report Status Update: {notificationTitle}",
                            $"Dear {report.User.FirstName},<br><br>" +
                            $"{notificationMessage}<br><br>" +
                            $"Response Notes: {notes}<br><br>" +
                            $"Best regards,<br>THYNK Team"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to send status update email: {ex.Message}");
                    }
                }
            }

            // Notify all clients about the report update
            await _hubContext.Clients.All.SendAsync("ReportUpdated", id, report.Status.ToString());
            await _hubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));
            await _communityHubContext.Clients.All.SendAsync("ReportUpdated", id, report.Status.ToString());
            await _communityHubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));
            
            TempData["SuccessMessage"] = "Report status has been updated successfully.";
            return RedirectToAction(nameof(ReportDetails), new { id = id });
        }

        // Create new alert
        public IActionResult CreateAlert()
        {
            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAlert(Alert alert, IFormFile alertImage, string backgroundStyle, string iconStyle, string colorScheme)
        {
            // Clear ModelState errors for fields we'll handle manually
            if (ModelState.ContainsKey("User"))
            {
                ModelState.Remove("User");
            }
            if (ModelState.ContainsKey("alertImage"))
            {
                ModelState.Remove("alertImage");
            }
            if (ModelState.ContainsKey("IssuedByUserId"))
            {
                ModelState.Remove("IssuedByUserId");
            }
            
            // Show specific validation errors instead of a generic message
            if (!ModelState.IsValid)
            {
                var errorMessages = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                
                // Log the specific errors
                foreach (var error in errorMessages)
                {
                    _logger.LogWarning($"Validation error: {error}");
                }
                
                if (errorMessages.Any())
                {
                    TempData["ErrorMessage"] = $"Validation errors: {string.Join(", ", errorMessages)}";
                }
                else
                {
                    TempData["ErrorMessage"] = "Please correct the form errors and try again.";
                }
                
                return View(alert);
            }
            
            try
            {
                // Get current user ID and explicitly set it
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    TempData["ErrorMessage"] = "User not authenticated. Please log in again.";
                    return View(alert);
                }
                
                // Ensure required fields have values
                if (string.IsNullOrEmpty(alert.Title))
                {
                    alert.Title = "Emergency Alert";
                    _logger.LogWarning("Alert Title was empty, using default value");
                }
                
                if (string.IsNullOrEmpty(alert.Message))
                {
                    alert.Message = "Please standby for further information.";
                    _logger.LogWarning("Alert Message was empty, using default value");
                }
                
                if (string.IsNullOrEmpty(alert.AffectedArea))
                {
                    alert.AffectedArea = "All areas";
                    _logger.LogWarning("AffectedArea was empty, using default value");
                }
                
                // Set required properties directly
                alert.DateIssued = DateTime.UtcNow;
                alert.IsActive = true;
                alert.IssuedByUserId = userId;
                
                // Set customization options if provided
                if (!string.IsNullOrEmpty(backgroundStyle)) alert.BackgroundStyle = backgroundStyle;
                if (!string.IsNullOrEmpty(iconStyle)) alert.IconStyle = iconStyle;
                if (!string.IsNullOrEmpty(colorScheme)) alert.ColorScheme = colorScheme;
                
                // Handle image - required field from model validation
                if (alertImage != null && alertImage.Length > 0)
                {
                    var webRootPath = _webHostEnvironment.WebRootPath;
                    var uploadsFolder = Path.Combine(webRootPath, "uploads", "alerts");
                    
                    // Create directory if it doesn't exist
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                        
                        // Generate unique filename
                    var uniqueFileName = $"{DateTime.Now.Ticks}_{Path.GetFileName(alertImage.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                        
                    // Save the file
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await alertImage.CopyToAsync(fileStream);
                        }
                        
                    // Store the path
                    alert.ImagePath = $"/uploads/alerts/{uniqueFileName}";
                }
                else 
                {
                    // If no image is provided, set a default image path
                    alert.ImagePath = "/images/default-alert.png";
                }
                
                // Add to context without tracking the User navigation property
                _context.Entry(alert).State = EntityState.Detached;
                var alertEntry = _context.Alerts.Add(alert);
                alertEntry.Reference(a => a.User).IsModified = false;
                
                try
                {
                    // Use synchronous SaveChanges as in TestDatabaseConnection
                    int result = _context.SaveChanges();
                    
                    if (result > 0)
                    {
                        // Broadcast the new alert to all connected clients using SignalR
                        try
                        {
                            // Fetch the complete alert with user details for broadcasting
                            var alertWithUser = _context.Alerts
                                .Include(a => a.User)
                                .FirstOrDefault(a => a.Id == alert.Id);

                            // Create a broadcast-safe version of the alert (to avoid serialization issues)
                            var broadcastAlert = new 
                            {
                                alertWithUser.Id,
                                alertWithUser.Title,
                                alertWithUser.Message,
                                alertWithUser.Severity,
                                alertWithUser.DateIssued,
                                alertWithUser.ExpiresAt,
                                alertWithUser.IsActive,
                                alertWithUser.AffectedArea,
                                alertWithUser.ImagePath,
                                alertWithUser.BackgroundStyle,
                                alertWithUser.IconStyle,
                                alertWithUser.ColorScheme,
                                User = new 
                                {
                                    Id = alertWithUser.User?.Id,
                                    Name = alertWithUser.User != null 
                                        ? $"{alertWithUser.User.FirstName} {alertWithUser.User.LastName}"
                                        : "System"
                                }
                            };

                            await _alertHubContext.Clients.All.SendAsync("ReceiveAlert", broadcastAlert);
                            _logger.LogInformation($"Alert {alert.Id} broadcast to all connected clients");
                        }
                        catch (Exception signalREx)
                        {
                            // Log but don't fail the operation if SignalR broadcasting fails
                            _logger.LogError(signalREx, "Error broadcasting alert via SignalR");
                        }

                        TempData["SuccessMessage"] = "Alert has been created successfully and broadcast to all users.";
                        
                        // Send SMS notifications to users
                        try
                        {
                            await _alertNotificationService.SendAlertNotifications(alert);
                            _logger.LogInformation($"SMS notifications sent for alert {alert.Id}");
                        }
                        catch (Exception smsEx)
                        {
                            // Log but don't fail the operation if SMS sending fails
                            _logger.LogError(smsEx, "Error sending SMS notifications for alert");
                        }
                        
                return RedirectToAction(nameof(ManageAlerts));
            }
                    else
                    {
                        TempData["ErrorMessage"] = "Database reported success but no records were inserted.";
            return View(alert);
                    }
                }
                catch (DbUpdateException dbEx)
                {
                    _logger.LogError(dbEx, "Database error creating alert");
                    if (dbEx.InnerException != null)
                    {
                        TempData["ErrorMessage"] = $"Database error: {dbEx.InnerException.Message}";
                    }
                    else
                    {
                        TempData["ErrorMessage"] = $"Database error: {dbEx.Message}";
                    }
                    return View(alert);
                }
            }
            catch (Exception ex)
            {
                // Log exception
                _logger.LogError(ex, "Error creating alert");
                TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
                return View(alert);
            }
        }

        // Manage alerts
        public async Task<IActionResult> ManageAlerts(bool? isActive = null)
        {
            IQueryable<Alert> alertsQuery = _context.Alerts;
            
            // Filter for active/inactive alerts if requested
            if (isActive.HasValue)
            {
                alertsQuery = alertsQuery.Where(a => a.IsActive == isActive.Value);
            }
            
            var alerts = await alertsQuery
                .OrderByDescending(a => a.DateIssued)
                .ToListAsync();
                
            ViewBag.CurrentFilter = isActive;
            return View(alerts);
        }

        [HttpGet]
        public async Task<IActionResult> EditAlert(int id)
        {
            var alert = await _context.Alerts.FindAsync(id);
            if (alert == null)
            {
                return NotFound();
            }
            return View(alert);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAlert(int id, Alert alert)
        {
            if (id != alert.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(alert);
                    await _context.SaveChangesAsync();
                    
                    // Broadcast the updated alert to all connected clients
                    try
                    {
                        // Fetch the complete alert with user details for broadcasting
                        var alertWithUser = await _context.Alerts
                            .Include(a => a.User)
                            .FirstOrDefaultAsync(a => a.Id == alert.Id);

                        if (alertWithUser != null)
                        {
                            // Create a broadcast-safe version of the alert
                            var broadcastAlert = new 
                            {
                                alertWithUser.Id,
                                alertWithUser.Title,
                                alertWithUser.Message,
                                alertWithUser.Severity,
                                alertWithUser.DateIssued,
                                alertWithUser.ExpiresAt,
                                alertWithUser.IsActive,
                                alertWithUser.AffectedArea,
                                alertWithUser.ImagePath,
                                alertWithUser.BackgroundStyle,
                                alertWithUser.IconStyle,
                                alertWithUser.ColorScheme,
                                User = new 
                                {
                                    Id = alertWithUser.User?.Id,
                                    Name = alertWithUser.User != null 
                                        ? $"{alertWithUser.User.FirstName} {alertWithUser.User.LastName}"
                                        : "System"
                                }
                            };

                            await _alertHubContext.Clients.All.SendAsync("AlertUpdated", broadcastAlert);
                            _logger.LogInformation($"Alert {alert.Id} update broadcast to all connected clients");
                        }
                    }
                    catch (Exception signalREx)
                    {
                        // Log but don't fail the operation if SignalR broadcasting fails
                        _logger.LogError(signalREx, "Error broadcasting alert update via SignalR");
                    }
                    
                    TempData["SuccessMessage"] = "Alert has been updated successfully.";
                    return RedirectToAction(nameof(ManageAlerts));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AlertExists(alert.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return View(alert);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAlert(int id)
        {
            var alert = await _context.Alerts.FindAsync(id);
            if (alert != null)
            {
                _context.Alerts.Remove(alert);
                await _context.SaveChangesAsync();
                
                // Broadcast the alert deletion to all connected clients
                try
                {
                    await _alertHubContext.Clients.All.SendAsync("AlertDeleted", id);
                    _logger.LogInformation($"Alert {id} deletion broadcast to all connected clients");
                }
                catch (Exception signalREx)
                {
                    // Log but don't fail the operation if SignalR broadcasting fails
                    _logger.LogError(signalREx, "Error broadcasting alert deletion via SignalR");
                }
                
                TempData["SuccessMessage"] = "Alert has been deleted successfully.";
            }
            return RedirectToAction(nameof(ManageAlerts));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateAlert(int id)
        {
            var alert = await _context.Alerts.FindAsync(id);
            if (alert == null)
            {
                return NotFound();
            }
            
            // Update alert status to inactive
            alert.IsActive = false;
            alert.ExpiresAt = DateTime.Now;
            
            _context.Update(alert);
            await _context.SaveChangesAsync();
            
            // Broadcast the updated alert to all connected clients
            try
            {
                // Fetch the complete alert with user details for broadcasting
                var alertWithUser = await _context.Alerts
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.Id == alert.Id);

                if (alertWithUser != null)
                {
                    // Create a broadcast-safe version of the alert
                    var broadcastAlert = new 
                    {
                        alertWithUser.Id,
                        alertWithUser.Title,
                        alertWithUser.Message,
                        alertWithUser.Severity,
                        alertWithUser.DateIssued,
                        alertWithUser.ExpiresAt,
                        alertWithUser.IsActive,
                        alertWithUser.AffectedArea,
                        alertWithUser.ImagePath,
                        alertWithUser.BackgroundStyle,
                        alertWithUser.IconStyle,
                        alertWithUser.ColorScheme,
                        User = new 
                        {
                            Id = alertWithUser.User?.Id,
                            Name = alertWithUser.User != null 
                                ? $"{alertWithUser.User.FirstName} {alertWithUser.User.LastName}"
                                : "System"
                        }
                    };

                    await _alertHubContext.Clients.All.SendAsync("AlertUpdated", broadcastAlert);
                    _logger.LogInformation($"Alert {alert.Id} update (deactivation) broadcast to all connected clients");
                }
            }
            catch (Exception signalREx)
            {
                // Log but don't fail the operation if SignalR broadcasting fails
                _logger.LogError(signalREx, "Error broadcasting alert deactivation via SignalR");
            }
            
            TempData["SuccessMessage"] = "Alert has been deactivated successfully.";
            return RedirectToAction(nameof(ManageAlerts));
        }

        [HttpGet]
        public async Task<IActionResult> GetAlertDetails(int id)
        {
            var alert = await _context.Alerts
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (alert == null)
            {
                return NotFound();
            }

            return PartialView("_AlertDetails", alert);
        }

        private bool AlertExists(int id)
        {
            return _context.Alerts.Any(e => e.Id == id);
        }

        // View incident map
        public IActionResult IncidentMap()
        {
            return View();
        }

        // Manage educational resources
        public async Task<IActionResult> ManageResources()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Get resources created by this user
            var resources = await _context.EducationalResources
                .Where(r => r.CreatedById == userId)
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();
                
            return View(resources);
        }

        // GET: Create Educational Resource
        public IActionResult CreateResource()
        {
            return View();
        }

        // POST: Create Educational Resource
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateResource(EducationalResource resource, IFormFile resourceFile)
        {
            try
            {
                // Validate the model (except for FileUrl which we'll handle manually)
                if (!ModelState.IsValid)
                {
                    // We'll manually remove FileUrl errors as we'll handle that separately
                    if (ModelState.ContainsKey("FileUrl"))
                    {
                        ModelState["FileUrl"].Errors.Clear();
                    }
                    
                    // If there are still errors after removing FileUrl errors
                    if (ModelState.Values.Any(v => v.Errors.Count > 0))
                    {
                        // Log validation errors
                        foreach (var modelState in ModelState.Values)
                        {
                            foreach (var error in modelState.Errors)
                            {
                                _logger.LogWarning($"Validation error: {error.ErrorMessage}");
                            }
                        }
                        
                        // Return the view with validation errors
                        return View(resource);
                    }
                }
                
                // Set creation date and creator
                resource.DateAdded = DateTime.Now;
                resource.CreatedById = User.FindFirstValue(ClaimTypes.NameIdentifier);
                
                // Process file if provided
                if (resourceFile != null && resourceFile.Length > 0)
                {
                    // Validate file size (max 10MB)
                    if (resourceFile.Length > 10 * 1024 * 1024)
                    {
                        ModelState.AddModelError("resourceFile", "File size must be less than 10MB.");
                        return View(resource);
                    }
                    
                    // Validate file type
                    var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".txt", ".jpg", ".jpeg", ".png" };
                    var fileExtension = Path.GetExtension(resourceFile.FileName).ToLowerInvariant();
                    
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        ModelState.AddModelError("resourceFile", $"File type {fileExtension} is not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");
                        return View(resource);
                    }
                    
                    try
                    {
                        // Create the resource directory if it doesn't exist
                        string resourceDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "education_resource");
                        if (!Directory.Exists(resourceDirectory))
                        {
                            Directory.CreateDirectory(resourceDirectory);
                        }

                        // Generate unique filename
                        string uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                        string filePath = Path.Combine(resourceDirectory, uniqueFileName);
                        
                        // Save the file
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await resourceFile.CopyToAsync(stream);
                        }
                        
                        // Set file URL and size
                        resource.FileUrl = $"/uploads/education_resource/{uniqueFileName}";
                        resource.FileSizeKB = (int)(resourceFile.Length / 1024);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"File upload error: {ex.Message}");
                        ModelState.AddModelError("resourceFile", $"Error uploading file: {ex.Message}");
                        return View(resource);
                    }
                }
                else
                {
                    // If no file was uploaded, set a placeholder URL
                    // This addresses the "FileUrl field is required" error
                    resource.FileUrl = string.Empty;
                    resource.FileSizeKB = 0;
                    
                    // Add a warning message
                    TempData["WarningMessage"] = "No file was attached to this resource. You can edit the resource later to add a file.";
                }
                
                // Default values for unused fields if null to satisfy required constraints
                if (string.IsNullOrEmpty(resource.ExternalUrl))
                {
                    resource.ExternalUrl = string.Empty;
                }
                
                if (string.IsNullOrEmpty(resource.Tags))
                {
                    resource.Tags = string.Empty;
                }
                
                // Set default approval status for new resources
                resource.ApprovalStatus = ApprovalStatus.Pending;
                
                // Save resource to database
                try
                {
                    _context.Add(resource);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "Educational resource created successfully! It is now pending approval.";
                    return RedirectToAction(nameof(ManageResources));
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Database error when saving resource: {ex.Message}");
                    ModelState.AddModelError("", $"Error saving to database: {ex.Message}");
                    return View(resource);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error in CreateResource: {ex.Message}");
                ModelState.AddModelError("", $"An unexpected error occurred: {ex.Message}");
                return View(resource);
            }
        }

        // GET: Edit Educational Resource
        public async Task<IActionResult> EditResource(int id)
        {
            var resource = await _context.EducationalResources.FindAsync(id);
            
            if (resource == null)
            {
                return NotFound();
            }
            
            return View(resource);
        }

        // POST: Edit Educational Resource
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditResource(int id, EducationalResource resource, IFormFile resourceFile)
        {
            if (id != resource.Id)
            {
                return NotFound();
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    // Get the existing resource to keep file info if not changing
                    var existingResource = await _context.EducationalResources.AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == id);
                        
                    if (existingResource == null)
                    {
                        return NotFound();
                    }
                    
                    // Keep existing file info if no new file uploaded
                    if (resourceFile == null || resourceFile.Length == 0)
                    {
                        resource.FileUrl = existingResource.FileUrl;
                        resource.FileSizeKB = existingResource.FileSizeKB;
                    }
                    else
                    {
                        // Process new file
                        string resourceDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "education_resource");
                        if (!Directory.Exists(resourceDirectory))
                        {
                            Directory.CreateDirectory(resourceDirectory);
                        }

                        // Delete old file if it exists
                        if (!string.IsNullOrEmpty(existingResource.FileUrl))
                        {
                            string oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                existingResource.FileUrl.TrimStart('/'));
                            if (System.IO.File.Exists(oldFilePath))
                            {
                                System.IO.File.Delete(oldFilePath);
                            }
                        }

                        // Save new file
                        string fileExtension = Path.GetExtension(resourceFile.FileName);
                        string uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                        string filePath = Path.Combine(resourceDirectory, uniqueFileName);
                        
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await resourceFile.CopyToAsync(stream);
                        }
                        
                        resource.FileUrl = $"/uploads/education_resource/{uniqueFileName}";
                        resource.FileSizeKB = (int)(resourceFile.Length / 1024);
                    }
                    
                    // Keep the original date added
                    resource.DateAdded = existingResource.DateAdded;
                    
                    // Default values for unused fields if null
                    if (string.IsNullOrEmpty(resource.ExternalUrl))
                    {
                        resource.ExternalUrl = string.Empty;
                    }
                    
                    if (string.IsNullOrEmpty(resource.FileUrl))
                    {
                        resource.FileUrl = string.Empty;
                    }
                    
                    if (string.IsNullOrEmpty(resource.Tags))
                    {
                        resource.Tags = string.Empty;
                    }
                    
                    _context.Update(resource);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "Educational resource updated successfully!";
                    return RedirectToAction(nameof(ManageResources));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ResourceExists(resource.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error updating resource: {ex.Message}");
                    ModelState.AddModelError("", "An error occurred while updating the resource.");
                }
            }
            
            return View(resource);
        }

        // GET: Delete Educational Resource Confirmation
        public async Task<IActionResult> DeleteResource(int id)
        {
            var resource = await _context.EducationalResources.FindAsync(id);
            
            if (resource == null)
            {
                return NotFound();
            }
            
            return View(resource);
        }

        // POST: Delete Educational Resource
        [HttpPost, ActionName("DeleteResource")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteResourceConfirmed(int id)
        {
            var resource = await _context.EducationalResources.FindAsync(id);
            
            if (resource == null)
            {
                return NotFound();
            }
            
            // Delete the file if it exists
            if (!string.IsNullOrEmpty(resource.FileUrl))
            {
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                    resource.FileUrl.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            
            _context.EducationalResources.Remove(resource);
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Educational resource deleted successfully!";
            return RedirectToAction(nameof(ManageResources));
        }

        // View resource details (visible to all users)
        [AllowAnonymous]
        public async Task<IActionResult> ViewResource(int id)
        {
            var resource = await _context.EducationalResources
                .FirstOrDefaultAsync(r => r.Id == id && r.ApprovalStatus == ApprovalStatus.Approved);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            // Track view count (optional)
            resource.ViewCount = (resource.ViewCount ?? 0) + 1;
            await _context.SaveChangesAsync();
            
            return View(resource);
        }

        // Resource Library (visible to all users)
        [AllowAnonymous]
        public async Task<IActionResult> ResourceLibrary()
        {
            var resources = await _context.EducationalResources
                .Where(r => r.ApprovalStatus == ApprovalStatus.Approved)
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();
                
            return View(resources);
        }
        
        private bool ResourceExists(int id)
        {
            return _context.EducationalResources.Any(e => e.Id == id);
        }

        // View community feed
        public async Task<IActionResult> CommunityFeed()
        {
            var updates = await _context.CommunityUpdates
                .Include(c => c.User)
                .Where(c => c.ModerationStatus == ModerationStatus.Approved)
                .OrderByDescending(c => c.DatePosted)
                .ToListAsync();
                
            return View(updates);
        }

        // Support page
        public IActionResult Support()
        {
            return View();
        }

        // Profile management page
        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Fetch the current user directly from the database instead of using UserManager
            // to ensure we get the most up-to-date data including ProfilePhotoUrl
            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Get or initialize notification preferences
            var notificationPreferences = await _context.NotificationPreferences
                .FirstOrDefaultAsync(np => np.UserId == userId);
                
            if (notificationPreferences == null)
            {
                notificationPreferences = new NotificationPreferences
                {
                    UserId = userId,
                    EmailEnabled = true,
                    SmsEnabled = false,
                    ReportUpdatesEnabled = true,
                    CommunityActivityEnabled = true
                };
                
                _context.NotificationPreferences.Add(notificationPreferences);
                await _context.SaveChangesAsync();
            }
            
            // Pass notification preferences to the view
            currentUser.NotificationPreferences = notificationPreferences;
            
            // Get available jurisdiction areas
            ViewBag.AvailableAreas = await _context.Users
                .Where(u => !string.IsNullOrEmpty(u.CityMunicipalityName))
                .Select(u => u.CityMunicipalityName)
                .Distinct()
                .ToListAsync();
                
            // Debug log to show the current profile photo URL
            _logger.LogInformation($"User {userId} has profile photo URL: {currentUser.ProfilePhotoUrl}");
            
            return View(currentUser);
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(LGUUser model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    // Update user fields
                    currentUser.FirstName = model.FirstName;
                    currentUser.LastName = model.LastName;
                    currentUser.Position = model.Position;
                    currentUser.Department = model.Department;
                    currentUser.Email = model.Email;
                    currentUser.PhoneNumber = model.PhoneNumber;
                    currentUser.Bio = model.Bio;
                    
                    // Update jurisdiction area if provided
                    if (!string.IsNullOrEmpty(model.JurisdictionArea))
                    {
                        currentUser.CityMunicipalityName = model.JurisdictionArea;
                    }
                    
                    // Save changes
                    await _userManager.UpdateAsync(currentUser);
                    
                    TempData["SuccessMessage"] = "Profile information updated successfully.";
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error updating profile: {ex.Message}");
                    ModelState.AddModelError("", "An error occurred while updating your profile.");
                }
            }
            
            return RedirectToAction(nameof(Profile));
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePassword(string CurrentPassword, string NewPassword, string ConfirmPassword)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId);
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(NewPassword) || string.IsNullOrEmpty(ConfirmPassword))
            {
                TempData["ErrorMessage"] = "All password fields are required.";
                return RedirectToAction(nameof(Profile));
            }
            
            if (NewPassword != ConfirmPassword)
            {
                TempData["ErrorMessage"] = "New password and confirmation password do not match.";
                return RedirectToAction(nameof(Profile));
            }
            
            var passwordCheck = await _userManager.CheckPasswordAsync(currentUser, CurrentPassword);
            if (!passwordCheck)
            {
                TempData["ErrorMessage"] = "Current password is incorrect.";
                return RedirectToAction(nameof(Profile));
            }
            
            // Change password
            var result = await _userManager.ChangePasswordAsync(currentUser, CurrentPassword, NewPassword);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Password updated successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to update password: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }
            
            return RedirectToAction(nameof(Profile));
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateNotifications(bool EmailNotifications, bool SmsNotifications, bool ReportUpdates, bool CommunityActivity)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Get existing notification preferences
            var preferences = await _context.NotificationPreferences
                .FirstOrDefaultAsync(np => np.UserId == userId);
                
            if (preferences == null)
            {
                // Create new notification preferences if they don't exist
                preferences = new NotificationPreferences
                {
                    UserId = userId,
                    EmailEnabled = EmailNotifications,
                    SmsEnabled = SmsNotifications,
                    ReportUpdatesEnabled = ReportUpdates,
                    CommunityActivityEnabled = CommunityActivity
                };
                
                _context.NotificationPreferences.Add(preferences);
            }
            else
            {
                // Update existing preferences
                preferences.EmailEnabled = EmailNotifications;
                preferences.SmsEnabled = SmsNotifications;
                preferences.ReportUpdatesEnabled = ReportUpdates;
                preferences.CommunityActivityEnabled = CommunityActivity;
                
                _context.NotificationPreferences.Update(preferences);
            }
            
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Notification preferences updated successfully.";
            
            return RedirectToAction(nameof(Profile));
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfilePhoto(IFormFile ProfilePhoto)
        {
            _logger.LogInformation("UpdateProfilePhoto method called");
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInformation($"User ID: {userId}");
            var currentUser = await _userManager.FindByIdAsync(userId);
            
            if (currentUser == null)
            {
                _logger.LogWarning("User not found");
                return NotFound();
            }
            
            if (ProfilePhoto != null && ProfilePhoto.Length > 0)
            {
                _logger.LogInformation($"Received file: {ProfilePhoto.FileName}, ContentType: {ProfilePhoto.ContentType}, Size: {ProfilePhoto.Length} bytes");
                
                // Check file size (max 2MB)
                if (ProfilePhoto.Length > 2 * 1024 * 1024)
                {
                    TempData["ErrorMessage"] = "Profile photo must be less than 2MB.";
                    return RedirectToAction(nameof(Profile));
                }
                
                // Check file type
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif" };
                if (!allowedTypes.Contains(ProfilePhoto.ContentType.ToLower()))
                {
                    TempData["ErrorMessage"] = "Only JPEG, PNG, and GIF images are allowed.";
                    return RedirectToAction(nameof(Profile));
                }
                
                try
                {
                    // Get wwwroot path
                    var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    _logger.LogInformation($"Web root path: {webRootPath}");
                    
                    // Create directory if it doesn't exist
                    var uploadsFolder = Path.Combine(webRootPath, "uploads", "profile_pics");
                    _logger.LogInformation($"Uploads folder path: {uploadsFolder}");
                    
                    if (!Directory.Exists(uploadsFolder))
                    {
                        _logger.LogInformation("Creating uploads directory");
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    
                    // Generate unique filename
                    var uniqueFileName = $"{userId}_{DateTime.Now.Ticks}{Path.GetExtension(ProfilePhoto.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    _logger.LogInformation($"Saving file to: {filePath}");
                    
                    // Save the file
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await ProfilePhoto.CopyToAsync(fileStream);
                        _logger.LogInformation("File saved successfully");
                    }
                    
                    // Update user's profile photo URL with the correct path
                    var photoUrl = $"/uploads/profile_pics/{uniqueFileName}";
                    _logger.LogInformation($"Setting ProfilePhotoUrl to: {photoUrl}");
                    
                    // Force image to be created with valid permissions
                    System.IO.File.SetAttributes(filePath, FileAttributes.Normal);
                    
                    // Update in both currentUser and dbUser
                    currentUser.ProfilePhotoUrl = photoUrl;
                    
                    // Update with UserManager
                    var result = await _userManager.UpdateAsync(currentUser);
                    if (!result.Succeeded)
                    {
                        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                        _logger.LogError($"Error updating user profile via UserManager: {errors}");
                    }
                    else
                    {
                        _logger.LogInformation("UserManager update succeeded");
                    }
                    
                    // Also directly update the database as a fallback
                    var dbUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                    if (dbUser != null)
                    {
                        dbUser.ProfilePhotoUrl = photoUrl;
                        var saveChangesResult = await _context.SaveChangesAsync();
                        _logger.LogInformation($"Updated profile photo URL directly in database. Result: {saveChangesResult}");
                    }
                    else
                    {
                        _logger.LogWarning("Could not find user in database context");
                    }
                    
                    // Try getting the user again to verify the update
                    var verifyUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                    _logger.LogInformation($"Verification check - ProfilePhotoUrl after update: {verifyUser?.ProfilePhotoUrl}");
                    
                    TempData["SuccessMessage"] = "Profile photo updated successfully.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating profile photo: {ex.Message}");
                    TempData["ErrorMessage"] = $"An error occurred while updating your profile photo: {ex.Message}";
                }
            }
            else
            {
                _logger.LogWarning("No file received or file is empty");
                TempData["ErrorMessage"] = "Please select a profile photo to upload.";
            }
            
            return RedirectToAction(nameof(Profile));
        }
        
        [HttpGet]
        public async Task<IActionResult> DeleteAccount()
        {
            return View();
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDeleteAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId);
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Sign the user out
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            
            // Attempt to delete the user
            var result = await _userManager.DeleteAsync(currentUser);
            
            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Home");
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to delete account: " + string.Join(", ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Profile));
            }
        }

        // API endpoint to get map data
        [HttpGet]
        public async Task<JsonResult> GetMapData(int? status, DateTime? dateFrom, DateTime? dateTo)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new List<object>());
            }

            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            if (currentUser == null)
            {
                return Json(new List<object>());
            }

            // Start with base query for incidents assigned to this LGU
            var query = _context.DisasterReports
                .Include(r => r.AssignedTo)
                .Where(r => r.AssignedToId == userId || 
                       (r.Status == ReportStatus.InProgress && r.AssignedTo != null && r.AssignedTo.OrganizationName == currentUser.OrganizationName));

            // Apply status filter if provided
            if (status.HasValue)
            {
                query = query.Where(r => (int)r.Status == status.Value);
            }
            else
            {
                // By default, only show In Progress and Resolved
                query = query.Where(r => r.Status == ReportStatus.InProgress || r.Status == ReportStatus.Resolved);
            }

            // Apply date filters if provided
            if (dateFrom.HasValue)
            {
                query = query.Where(r => r.DateReported >= dateFrom.Value);
            }
            if (dateTo.HasValue)
            {
                query = query.Where(r => r.DateReported <= dateTo.Value);
            }

            // Get the filtered incidents
            var incidents = await query
                .OrderByDescending(r => r.DateReported)
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Description,
                    r.Location,
                    r.Barangay,
                    r.City,
                    r.Latitude,
                    r.Longitude,
                    r.Type,
                    r.Status,
                    r.Severity,
                    r.DateReported,
                    AssignedTo = r.AssignedTo != null ? new
                    {
                        r.AssignedTo.Id,
                        Name = r.AssignedTo.FirstName + " " + r.AssignedTo.LastName,
                        Organization = r.AssignedTo.OrganizationName
                    } : null
                })
                .ToListAsync();

            return Json(incidents);
        }

        // Post a community update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostUpdate(CommunityUpdate update, IFormFile Attachment)
        {
            try
            {
                // Get the current user's ID
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User not authenticated");
                    TempData["ErrorMessage"] = "User not authenticated.";
                    return View(update);
                }

                _logger.LogInformation($"User ID: {userId}");

                var user = await _userManager.FindByIdAsync(userId) as LGUUser;
                if (user == null)
                {
                    _logger.LogWarning($"PostUpdate failed: User not found for ID {userId}");
                    return NotFound();
                }

                // Set the required fields
                update.UserId = userId;
                update.DatePosted = DateTime.Now;
                update.ModerationStatus = ModerationStatus.Approved; // Automatically approve LGU/SLU posts
                update.ImageUrl = null; // No default attachment

                // Set optional fields if not provided
                if (string.IsNullOrEmpty(update.Location))
                {
                    update.Location = "Not specified";
                }

                _logger.LogInformation($"Created CommunityUpdate object: {JsonSerializer.Serialize(update)}");

                // Handle file attachment if provided
                if (Attachment != null && Attachment.Length > 0)
                {
                    _logger.LogInformation($"Processing file upload: {Attachment.FileName}, Size: {Attachment.Length}");
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "community");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueFileName = $"{Guid.NewGuid()}_{Attachment.FileName}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await Attachment.CopyToAsync(fileStream);
                    }

                    update.ImageUrl = $"/uploads/community/{uniqueFileName}";
                    _logger.LogInformation($"File saved to: {update.ImageUrl}");
                }

                _context.CommunityUpdates.Add(update);
                _logger.LogInformation("Added update to context");

                var saveResult = await _context.SaveChangesAsync();
                _logger.LogInformation($"SaveChangesAsync result: {saveResult} rows affected");

                TempData["SuccessMessage"] = "Your post has been published successfully.";
                return RedirectToAction(nameof(CommunityFeed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PostUpdate action");
                TempData["ErrorMessage"] = "An error occurred while posting your update. Please try again.";
                return View(update);
            }
        }

        // Get recent reports for partial view update
        [HttpGet]
        public async Task<IActionResult> GetRecentReports()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var reports = await _context.DisasterReports
                .Where(r => r.AssignedToId == userId)
                .OrderByDescending(r => r.DateReported)
                .Take(5)
                .ToListAsync();

            return PartialView("_LGURecentReports", reports);
        }

        // Get count of recent reports
        [HttpGet]
        public async Task<IActionResult> GetRecentReportsCount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var count = await _context.DisasterReports
                .Where(r => r.AssignedToId == userId)
                .OrderByDescending(r => r.DateReported)
                .Take(5)
                .CountAsync();

            return Json(new { count });
        }

        // Get count of in-progress reports
        [HttpGet]
        public async Task<IActionResult> GetInProgressReportsCount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var count = await _context.DisasterReports
                .CountAsync(r => r.AssignedToId == userId && r.Status == ReportStatus.InProgress);

            return Json(new { count });
        }

        // Analytics dashboard
        public async Task<IActionResult> Analytics()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Get total reports count
            var totalReports = await _context.DisasterReports
                .Where(r => r.AssignedToId == userId || 
                           (r.AssignedTo.OrganizationName == currentUser.OrganizationName))
                .CountAsync();
                
            // Get resolved reports count
            var resolvedReports = await _context.DisasterReports
                .Where(r => (r.AssignedToId == userId || 
                           (r.AssignedTo.OrganizationName == currentUser.OrganizationName)) &&
                           r.Status == ReportStatus.Resolved)
                .CountAsync();
                
            // Calculate average response time (in hours)
            var reportsWithResolution = await _context.DisasterReports
                .Where(r => (r.AssignedToId == userId || 
                           (r.AssignedTo.OrganizationName == currentUser.OrganizationName)) &&
                           r.Status == ReportStatus.Resolved &&
                           r.ResolvedAt.HasValue &&
                           r.AssignedAt.HasValue)
                .ToListAsync();
                
            double avgResponseTime = 0;
            if (reportsWithResolution.Count > 0)
            {
                avgResponseTime = reportsWithResolution
                    .Average(r => (r.ResolvedAt.Value - r.AssignedAt.Value).TotalHours);
            }
            
            // Calculate community engagement (percentage of reports with comments)
            var communityEngagement = 0;
            // Implementation would depend on how comments are tracked in your system
            
            // Get geographic data for the map (in Negros Occidental area)
            var geoData = await _context.DisasterReports
                .Where(r => r.AssignedToId == userId || 
                           (r.AssignedTo.OrganizationName == currentUser.OrganizationName))
                .Where(r => r.Latitude != 0 && r.Longitude != 0)
                // Focus on Negros Occidental by filtering based on coordinates
                .Where(r => r.Longitude >= 122.27 && r.Longitude <= 123.55 && 
                           r.Latitude >= 9.85 && r.Latitude <= 11.05)
                .Select(r => new {
                    r.Id,
                    r.Title,
                    r.Type,
                    r.Status,
                    r.Severity,
                    r.Latitude,
                    r.Longitude,
                    r.Location,
                    r.Barangay,
                    r.City,
                    ReportDate = r.DateReported,
                    IsResolved = r.Status == ReportStatus.Resolved
                })
                .ToListAsync();
            
            _logger.LogInformation($"Retrieved {geoData.Count} geographic data points for Negros Occidental");
            
            // Calculate district-based counts (for Negros Occidental)
            // Simplified approach - in real implementation you'd have proper district mapping
            var northDistrictCount = geoData.Count(r => 
                r.City?.ToLower().Contains("sagay") == true || 
                r.City?.ToLower().Contains("cadiz") == true || 
                r.City?.ToLower().Contains("escalante") == true ||
                r.City?.ToLower().Contains("san carlos") == true);
                
            var centralDistrictCount = geoData.Count(r => 
                r.City?.ToLower().Contains("bacolod") == true ||
                r.City?.ToLower().Contains("talisay") == true ||
                r.City?.ToLower().Contains("silay") == true);
                
            var southDistrictCount = geoData.Count(r => 
                r.City?.ToLower().Contains("himamaylan") == true ||
                r.City?.ToLower().Contains("kabankalan") == true ||
                r.City?.ToLower().Contains("sipalay") == true);
            
            ViewBag.TotalReports = totalReports;
            ViewBag.ResolvedReports = resolvedReports;
            ViewBag.AverageResponseTime = $"{avgResponseTime:F1}h";
            ViewBag.CommunityEngagement = $"{communityEngagement}%";
            ViewBag.GeoData = geoData;
            ViewBag.NorthDistrictCount = northDistrictCount;
            ViewBag.CentralDistrictCount = centralDistrictCount;
            ViewBag.SouthDistrictCount = southDistrictCount;
            
            return View();
        }
        
        // Chat Support for LGU
        public async Task<IActionResult> ChatSupport(int? chatId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Extract LGU jurisdiction details from the current user
            string lgaName = currentUser.OrganizationName;
            
            // Get chats that match this LGU's jurisdiction
            var activeChats = await _context.ChatSessions
                .Include(c => c.User)
                .Include(c => c.Messages)
                .Where(c => c.Status != ChatStatus.Closed)
                .Where(c => 
                    // Already assigned to this specific LGU user
                    c.AssignedToId == userId ||
                    // OR not assigned yet AND matches LGU jurisdiction
                    (c.AssignedToId == null && 
                     (
                         // Match based on user's location (city/municipality)
                         (c.User != null && 
                          ((c.User.CityMunicipalityName != null && c.User.CityMunicipalityName.Contains(currentUser.CityMunicipalityName ?? "")) ||
                           (c.User.BarangayName != null && c.User.BarangayName.Contains(currentUser.BarangayName ?? "")))
                         ) ||
                         // OR match based on chat category (can contain LGU name or area)
                         (c.Category != null && c.Category.Contains(lgaName)) ||
                         // OR match based on chat title (can mention area name)
                         (c.Title != null && (
                             c.Title.Contains(currentUser.CityMunicipalityName ?? "") || 
                             c.Title.Contains(currentUser.BarangayName ?? "") ||
                             c.Title.Contains(lgaName)
                         ))
                     )
                    )
                )
                .OrderByDescending(c => c.StartTime)
                .ToListAsync();
                
            ViewBag.ActiveChats = activeChats;
            
            // If a specific chat is selected, get its details
            if (chatId.HasValue)
            {
                var selectedChat = await _context.ChatSessions
                    .Include(c => c.User)
                    .Include(c => c.Messages.OrderBy(m => m.Timestamp))
                    .FirstOrDefaultAsync(c => c.Id == chatId.Value);
                    
                if (selectedChat != null)
                {
                    // Verify this chat belongs to this LGU's jurisdiction before allowing access
                    bool isAssignedToThisUser = selectedChat.AssignedToId == userId;
                    bool matchesJurisdiction = false;
                    
                    if (selectedChat.User != null)
                    {
                        matchesJurisdiction = 
                            (selectedChat.User.CityMunicipalityName != null && selectedChat.User.CityMunicipalityName.Contains(currentUser.CityMunicipalityName ?? "")) ||
                            (selectedChat.User.BarangayName != null && selectedChat.User.BarangayName.Contains(currentUser.BarangayName ?? ""));
                    }
                    
                    bool matchesCategory = selectedChat.Category != null && selectedChat.Category.Contains(lgaName);
                    bool matchesTitle = selectedChat.Title != null && (
                        selectedChat.Title.Contains(currentUser.CityMunicipalityName ?? "") || 
                        selectedChat.Title.Contains(currentUser.BarangayName ?? "") ||
                        selectedChat.Title.Contains(lgaName)
                    );
                    
                    if (isAssignedToThisUser || matchesJurisdiction || matchesCategory || matchesTitle)
                    {
                        // Auto-assign this chat to the current LGU user if not already assigned
                        if (string.IsNullOrEmpty(selectedChat.AssignedToId))
                        {
                            selectedChat.AssignedToId = userId;
                            await _context.SaveChangesAsync();
                            
                            // Add system message indicating assignment
                            var assignmentMessage = new ChatMessage
                            {
                                ChatSessionId = selectedChat.Id,
                                SenderId = null,  // System message
                                Content = $"Chat assigned to {currentUser.FirstName} {currentUser.LastName} ({currentUser.OrganizationName}).",
                                Timestamp = DateTime.Now
                            };
                            
                            _context.ChatMessages.Add(assignmentMessage);
                            await _context.SaveChangesAsync();
                        }
                        
                        ViewBag.SelectedChat = selectedChat;
                        ViewBag.SelectedChatId = selectedChat.Id;
                    }
                    else
                    {
                        // This chat doesn't belong to this LGU's jurisdiction
                        TempData["ErrorMessage"] = "You don't have access to this chat as it's not in your jurisdiction.";
                    }
                }
            }
            
            return View();
        }
        
        // Send message in chat for LGU
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int sessionId, string message)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(c => c.Id == sessionId);
                
            if (session == null)
            {
                return NotFound();
            }
            
            if (session.Status == ChatStatus.Closed)
            {
                TempData["ErrorMessage"] = "This chat session has been closed.";
                return RedirectToAction(nameof(ChatSupport), new { chatId = sessionId });
            }
            
            // Verify this LGU user has jurisdiction over this chat
            bool hasJurisdiction = session.AssignedToId == userId;
            
            if (!hasJurisdiction)
            {
                // Check if chat matches jurisdiction based on location
                var chatUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == session.UserId);
                if (chatUser != null)
                {
                    hasJurisdiction = 
                        (chatUser.CityMunicipalityName != null && 
                         chatUser.CityMunicipalityName.Contains(currentUser.CityMunicipalityName ?? "")) ||
                        (chatUser.BarangayName != null && 
                         chatUser.BarangayName.Contains(currentUser.BarangayName ?? ""));
                }
                
                // Also check title and category
                hasJurisdiction = hasJurisdiction || 
                    (session.Category != null && session.Category.Contains(currentUser.OrganizationName)) ||
                    (session.Title != null && (
                        session.Title.Contains(currentUser.CityMunicipalityName ?? "") || 
                        session.Title.Contains(currentUser.BarangayName ?? "") ||
                        session.Title.Contains(currentUser.OrganizationName)
                    ));
            }
            
            if (!hasJurisdiction)
            {
                TempData["ErrorMessage"] = "You don't have access to this chat as it's not in your jurisdiction.";
                return RedirectToAction(nameof(ChatSupport));
            }
            
            // Add the message
            var chatMessage = new ChatMessage
            {
                ChatSessionId = sessionId,
                SenderId = userId,
                Content = message,
                Timestamp = DateTime.Now
            };
            
            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();
            
            // Always ensure this chat is assigned to the current LGU
            if (string.IsNullOrEmpty(session.AssignedToId))
            {
                session.AssignedToId = userId;
                await _context.SaveChangesAsync();
                
                // Add system message indicating assignment
                var assignmentMessage = new ChatMessage
                {
                    ChatSessionId = sessionId,
                    SenderId = null,  // System message
                    Content = $"Chat assigned to {currentUser.FirstName} {currentUser.LastName} ({currentUser.OrganizationName}).",
                    Timestamp = DateTime.Now
                };
                
                _context.ChatMessages.Add(assignmentMessage);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(ChatSupport), new { chatId = sessionId });
        }
        
        // Close chat for LGU
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseChat(int sessionId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(c => c.Id == sessionId);
                
            if (session == null)
            {
                return NotFound();
            }
            
            // Verify this LGU user has jurisdiction over this chat
            bool hasJurisdiction = session.AssignedToId == userId;
            
            if (!hasJurisdiction)
            {
                TempData["ErrorMessage"] = "You don't have access to this chat as it's not in your jurisdiction.";
                return RedirectToAction(nameof(ChatSupport));
            }
            
            // Close the session
            session.Status = ChatStatus.Closed;
            session.EndTime = DateTime.Now;
            
            // Add system message indicating session closure
            var closureMessage = new ChatMessage
            {
                ChatSessionId = sessionId,
                SenderId = null,
                Content = $"Chat session closed by {currentUser.FirstName} {currentUser.LastName} from {currentUser.OrganizationName}.",
                Timestamp = DateTime.Now
            };
            
            _context.ChatMessages.Add(closureMessage);
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(ChatSupport));
        }
        
        // Chat History for LGU
        public async Task<IActionResult> ChatHistory()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Extract LGU jurisdiction details from the current user
            string lgaName = currentUser.OrganizationName;
            
            // Get all chats (including closed ones) that were assigned to this LGU user
            // or match this LGU's jurisdiction
            var historicalChats = await _context.ChatSessions
                .Include(c => c.User)
                .Include(c => c.Messages)
                .Where(c => 
                    // Assigned to this specific LGU user
                    c.AssignedToId == userId ||
                    // OR matches LGU jurisdiction
                    (
                        // Match based on user's location (city/municipality)
                        (c.User != null && 
                         ((c.User.CityMunicipalityName != null && c.User.CityMunicipalityName.Contains(currentUser.CityMunicipalityName ?? "")) ||
                          (c.User.BarangayName != null && c.User.BarangayName.Contains(currentUser.BarangayName ?? "")))
                        ) ||
                        // OR match based on chat category (can contain LGU name or area)
                        (c.Category != null && c.Category.Contains(lgaName)) ||
                        // OR match based on chat title (can mention area name)
                        (c.Title != null && (
                            c.Title.Contains(currentUser.CityMunicipalityName ?? "") || 
                            c.Title.Contains(currentUser.BarangayName ?? "") ||
                            c.Title.Contains(lgaName)
                        ))
                    )
                )
                .OrderByDescending(c => c.StartTime)
                .ToListAsync();
            
            return View(historicalChats);
        }

        [HttpGet]
        public IActionResult TestFileSystem()
        {
            var result = new Dictionary<string, string>();
            
            try
            {
                // Test wwwroot path
                var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                result["WebRootPath"] = webRootPath;
                result["WebRootExists"] = Directory.Exists(webRootPath).ToString();
                
                // Test uploads directory
                var uploadsPath = Path.Combine(webRootPath, "uploads");
                result["UploadsPath"] = uploadsPath;
                result["UploadsExists"] = Directory.Exists(uploadsPath).ToString();
                
                // Test profile_pics directory
                var profilePicsPath = Path.Combine(uploadsPath, "profile_pics");
                result["ProfilePicsPath"] = profilePicsPath;
                result["ProfilePicsExists"] = Directory.Exists(profilePicsPath).ToString();
                
                // Try to create a test file
                var testFilePath = Path.Combine(profilePicsPath, "test.txt");
                System.IO.File.WriteAllText(testFilePath, "Test file created at " + DateTime.Now);
                result["TestFileCreated"] = "True";
                result["TestFilePath"] = testFilePath;
                
                // Try to read the test file
                var fileContent = System.IO.File.ReadAllText(testFilePath);
                result["TestFileContent"] = fileContent;
                
                // Get current user information
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var currentUser = _userManager.FindByIdAsync(userId).Result;
                
                if (currentUser != null)
                {
                    result["UserId"] = userId;
                    result["UserEmail"] = currentUser.Email;
                    result["CurrentProfilePhoto"] = currentUser.ProfilePhotoUrl ?? "null";
                }
                else
                {
                    result["UserInfo"] = "User not found";
                }
                
                // Try to get user from database context directly
                var dbUser = _context.Users.FirstOrDefault(u => u.Id == userId);
                if (dbUser != null)
                {
                    result["DbUserFound"] = "True";
                    result["DbUserProfilePhoto"] = dbUser.ProfilePhotoUrl ?? "null";
                }
                else
                {
                    result["DbUserFound"] = "False";
                }
            }
            catch (Exception ex)
            {
                result["Error"] = ex.Message;
                result["StackTrace"] = ex.StackTrace;
            }
            
            return Json(result);
        }

        private DateTime GetPhilippineTime()
        {
            return DateTime.UtcNow.AddHours(8); // Convert UTC to Philippine Time (UTC+8)
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveReport(int id, string resolution)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null)
            {
                return NotFound();
            }

            report.Status = ReportStatus.Resolved;
            report.Resolution = resolution;
            report.ResolvedAt = GetPhilippineTime();
            await _context.SaveChangesAsync();

            // Create notification for report resolution
            if (report.User != null)
            {
                var notification = new UserNotification
                {
                    UserId = report.UserId,
                    Title = "Report Resolved",
                    Message = $"Your report '{report.Title}' has been successfully resolved.",
                    NotificationType = "success",
                    RelatedEntityId = report.Id,
                    RelatedEntityType = "Report",
                    IsRead = false,
                    CreatedAt = GetPhilippineTime()
                };

                _context.UserNotifications.Add(notification);
                await _context.SaveChangesAsync();

                // Send notifications through both hubs to ensure real-time updates
                try
                {
                    // Send through admin hub
                    await _hubContext.Clients.Group($"user_{report.UserId}")
                        .SendAsync("NotificationReceived", notification);

                    // Send through community hub
                    await _communityHubContext.Clients.Group($"user_{report.UserId}")
                        .SendAsync("NotificationReceived", notification);

                    // Send status update through both hubs
                    await _hubContext.Clients.Group($"user_{report.UserId}")
                        .SendAsync("ReportStatusUpdated", report.Id, "Resolved", report.Title);
                    await _communityHubContext.Clients.Group($"user_{report.UserId}")
                        .SendAsync("ReportStatusUpdated", report.Id, "Resolved", report.Title);

                    // Notify all clients about the report update
                    await _hubContext.Clients.All.SendAsync("ReportUpdated", id, "Resolved");
                    await _communityHubContext.Clients.All.SendAsync("ReportUpdated", id, "Resolved");
                    await _hubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));
                    await _communityHubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));

                    _logger.LogInformation($"Successfully sent notifications for report {report.Id} resolution");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error sending notifications for report {report.Id}: {ex.Message}");
                }

                // Get notification preferences
                var notificationPreferences = await _context.NotificationPreferences
                    .FirstOrDefaultAsync(np => np.UserId == report.UserId);

                // Send email notification if enabled
                if (notificationPreferences?.EmailEnabled == true && !string.IsNullOrEmpty(report.User.Email))
                {
                    try
                    {
                        await _emailSender.SendEmailAsync(
                            report.User.Email,
                            "THYNK - Report Resolved",
                            $"Dear {report.User.FirstName},<br><br>" +
                            $"Your report '{report.Title}' has been successfully resolved.<br><br>" +
                            $"Resolution Notes: {resolution}<br><br>" +
                            $"Best regards,<br>THYNK Team"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to send resolution email: {ex.Message}");
                    }
                }
            }
            
            TempData["SuccessMessage"] = "Report has been resolved successfully.";
            return RedirectToAction(nameof(ReportDetails), new { id = id });
        }

        // Evacuation Sites
        public async Task<IActionResult> EvacuationSites(string type, bool? isActive)
        {
            IQueryable<EvacuationSite> sitesQuery = _context.EvacuationSites
                .Include(e => e.ManagedBy);
            
            // Filter by type if specified
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<EvacuationSiteType>(type, out var typeEnum))
            {
                sitesQuery = sitesQuery.Where(e => e.Type == typeEnum);
                ViewBag.CurrentTypeFilter = type;
            }
            
            // Filter by active status if specified
            if (isActive.HasValue)
            {
                sitesQuery = sitesQuery.Where(e => e.IsActive == isActive.Value);
                ViewBag.CurrentActiveFilter = isActive.Value;
            }
            
            var sites = await sitesQuery.OrderBy(e => e.Name).ToListAsync();
            return View(sites);
        }

        public IActionResult CreateEvacuationSite()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEvacuationSite(EvacuationSite site)
        {
            try
            {
                _logger.LogInformation($"Attempting to create evacuation site: {site.Name}, Type: {site.Type}, Lat: {site.Latitude}, Lng: {site.Longitude}");
                
                // Clear ModelState errors for ManagedBy and ManagedByUserId since we'll set them manually
                ModelState.Remove("ManagedBy");
                ModelState.Remove("ManagedByUserId");
                
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model state is invalid for evacuation site creation");
                    
                    // Log all model validation errors
                    foreach (var modelState in ModelState.Values)
                    {
                        foreach (var error in modelState.Errors)
                        {
                            _logger.LogWarning($"Validation error: {error.ErrorMessage}");
                        }
                    }
                    
                    ViewBag.ErrorMessage = "Please correct the validation errors below.";
                    return View(site);
                }
                
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                
                // Explicitly set the required properties
                site.ManagedByUserId = userId;
                site.DateAdded = DateTime.Now;
                site.IsActive = true;
                
                // Check if the user exists in the database
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogError($"Failed to create evacuation site: User with ID {userId} not found in the database");
                    ViewBag.ErrorMessage = "Unable to assign site to current user. Your login session may have expired.";
                    return View(site);
                }
                
                try 
                {
                    // First add the site without tracking ManagedBy
                    _context.Entry(site).State = EntityState.Added;
                    _context.Entry(site).Reference(s => s.ManagedBy).IsModified = false;
                    
                    // Explicitly log what we're trying to save
                    _logger.LogInformation($"Saving site to database: ID: {site.Id}, Name: {site.Name}, Address: {site.Address}, City: {site.City}, " +
                                          $"Coords: ({site.Latitude}, {site.Longitude}), Type: {site.Type}, Managed by: {site.ManagedByUserId}");
                    
                    int result = await _context.SaveChangesAsync();
                    _logger.LogInformation($"SaveChangesAsync result: {result} records affected");
                    
                    if (result > 0)
                    {
                        TempData["SuccessMessage"] = "Evacuation site has been created successfully.";
                        return RedirectToAction(nameof(EvacuationSites));
                    }
                    else
                    {
                        // If SaveChangesAsync returned 0, no records were affected
                        _logger.LogError("Database save operation did not affect any records");
                        ViewBag.ErrorMessage = "Failed to save the evacuation site to the database. No records were affected.";
                        return View(site);
                    }
                } 
                catch (InvalidOperationException ex) 
                {
                    _logger.LogError($"Entity Framework operation error: {ex.Message}");
                    
                    // Try an alternative approach if EF is having issues
                    try 
                    {
                        // Fallback to a simpler approach
                        _context.EvacuationSites.Add(site);
                        int result = await _context.SaveChangesAsync();
                        
                        if (result > 0) 
                        {
                            TempData["SuccessMessage"] = "Evacuation site has been created successfully.";
                            return RedirectToAction(nameof(EvacuationSites));
                        }
                        else
                        {
                            ViewBag.ErrorMessage = "Failed to save evacuation site with fallback approach.";
                            return View(site);
                        }
                    } 
                    catch (Exception innerEx) 
                    {
                        _logger.LogError($"Fallback approach also failed: {innerEx.Message}");
                        ViewBag.ErrorMessage = $"Could not save evacuation site: {ex.Message}";
                        return View(site);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error creating evacuation site: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                
                ViewBag.ErrorMessage = "An unexpected error occurred. Please try again or contact support.";
                return View(site);
            }
        }

        public async Task<IActionResult> EditEvacuationSite(int id)
        {
            try 
            {
                var site = await _context.EvacuationSites.FindAsync(id);
                if (site == null)
                {
                    _logger.LogWarning($"Evacuation site with ID {id} not found");
                    return NotFound();
                }
                
                return View(site);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving evacuation site {id}: {ex.Message}");
                TempData["ErrorMessage"] = "Error retrieving evacuation site. Please try again.";
                return RedirectToAction(nameof(EvacuationSites));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEvacuationSite(int id, EvacuationSite site)
        {
            if (id != site.Id)
            {
                _logger.LogWarning($"ID mismatch in EditEvacuationSite: URL ID={id}, Model ID={site.Id}");
                return NotFound();
            }
            
            try
            {
                _logger.LogInformation($"Attempting to update evacuation site {id}: {site.Name}, Type: {site.Type}, Lat: {site.Latitude}, Lng: {site.Longitude}");
                
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Model state is invalid for evacuation site update");
                    
                    // Log all model validation errors
                    foreach (var modelState in ModelState.Values)
                    {
                        foreach (var error in modelState.Errors)
                        {
                            _logger.LogWarning($"Validation error: {error.ErrorMessage}");
                        }
                    }
                    
                    ViewBag.ErrorMessage = "Please correct the validation errors below.";
                    return View(site);
                }
                
                var originalSite = await _context.EvacuationSites.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
                if (originalSite == null)
                {
                    _logger.LogWarning($"Original evacuation site with ID {id} not found during update");
                    return NotFound();
                }
                
                // Preserve the original creation data
                site.ManagedByUserId = originalSite.ManagedByUserId;
                site.DateAdded = originalSite.DateAdded;
                site.LastUpdated = DateTime.Now;
                
                _context.Update(site);
                
                _logger.LogInformation($"Updating site in database: ID: {site.Id}, Name: {site.Name}, Address: {site.Address}, City: {site.City}, " +
                                     $"Coords: ({site.Latitude}, {site.Longitude}), Type: {site.Type}, Managed by: {site.ManagedByUserId}");
                
                int result = await _context.SaveChangesAsync();
                _logger.LogInformation($"SaveChangesAsync result: {result} records affected");
                
                if (result > 0)
                {
                    TempData["SuccessMessage"] = "Evacuation site has been updated successfully.";
                    return RedirectToAction(nameof(EvacuationSites));
                }
                else
                {
                    // If SaveChangesAsync returned 0, no records were affected
                    _logger.LogError("Database update operation did not affect any records");
                    ViewBag.ErrorMessage = "Failed to update the evacuation site. No records were affected.";
                    return View(site);
                }
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (!EvacuationSiteExists(site.Id))
                {
                    _logger.LogWarning($"Evacuation site {site.Id} not found during concurrency check");
                    return NotFound();
                }
                else
                {
                    _logger.LogError($"Concurrency exception updating evacuation site {id}: {ex.Message}");
                    ViewBag.ErrorMessage = "The evacuation site was modified by another user. Please try again.";
                    return View(site);
                }
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError($"Database error updating evacuation site {id}: {ex.Message}");
                _logger.LogError($"Inner exception: {ex.InnerException?.Message}");
                
                // Get detailed entity validation errors if available
                if (ex.InnerException != null)
                {
                    _logger.LogError($"SQL error details: {ex.InnerException.Message}");
                }
                
                ViewBag.ErrorMessage = $"Database error: {ex.Message}. Please check your input and try again.";
                return View(site);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error updating evacuation site {id}: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                
                ViewBag.ErrorMessage = "An unexpected error occurred. Please try again or contact support.";
                return View(site);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleEvacuationSiteStatus(int id)
        {
            var site = await _context.EvacuationSites.FindAsync(id);
            if (site == null)
            {
                return NotFound();
            }
            
            site.IsActive = !site.IsActive;
            site.LastUpdated = DateTime.Now;
            
            await _context.SaveChangesAsync();
            
            string status = site.IsActive ? "active" : "inactive";
            TempData["SuccessMessage"] = $"Evacuation site has been marked as {status}.";
            
            return RedirectToAction(nameof(EvacuationSites));
        }

        private bool EvacuationSiteExists(int id)
        {
            return _context.EvacuationSites.Any(e => e.Id == id);
        }
    }
} 