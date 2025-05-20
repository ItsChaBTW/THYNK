using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using THYNK.Data;
using THYNK.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using THYNK.Hubs;
using System.Security.Claims;
using TimeZoneConverter;
using THYNK.Services;

namespace THYNK.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IEmailSender _emailSender;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<AdminController> _logger;
        private readonly IHubContext<AdminHub> _hubContext;
        private readonly IHubContext<CommunityHub> _communityHubContext;
        private readonly AlertNotificationService _alertNotificationService;

        public AdminController(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            IWebHostEnvironment webHostEnvironment,
            ILogger<AdminController> logger,
            IHubContext<AdminHub> hubContext,
            IHubContext<CommunityHub> communityHubContext,
            AlertNotificationService alertNotificationService)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
            _hubContext = hubContext;
            _communityHubContext = communityHubContext;
            _alertNotificationService = alertNotificationService;
        }

        private async Task UpdateDashboardStats()
        {
            var pendingLGUCount = await _context.Users.OfType<LGUUser>().CountAsync(u => !u.IsApproved);
            var pendingReportsCount = await _context.DisasterReports.CountAsync(r => r.Status == ReportStatus.Pending);
            var pendingPostsCount = await _context.CommunityUpdates.CountAsync(p => p.ModerationStatus == ModerationStatus.Pending);

            await _hubContext.Clients.All.SendAsync("ReceiveDashboardStats", pendingLGUCount, pendingReportsCount, pendingPostsCount);
        }

        private DateTime GetPhilippineTime()
        {
            var phTimeZone = TZConvert.GetTimeZoneInfo("Asia/Manila");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);
        }

        // Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var pendingLGUCount = await _context.Users.OfType<LGUUser>().CountAsync(u => !u.IsApproved);
            var pendingReportsCount = await _context.DisasterReports.CountAsync(r => r.Status == ReportStatus.Pending);
            var pendingPostsCount = await _context.CommunityUpdates.CountAsync(p => p.ModerationStatus == ModerationStatus.Pending);
            var activeUsersCount = await _context.Users.CountAsync(); // No LastLoginDate, so just count all users

            ViewBag.PendingLGUCount = pendingLGUCount;
            ViewBag.PendingReportsCount = pendingReportsCount;
            ViewBag.PendingPostsCount = pendingPostsCount;
            ViewBag.ActiveUsersCount = activeUsersCount;

            // Get recent reports
            ViewBag.RecentReports = await _context.DisasterReports
                .OrderByDescending(r => r.DateReported)
                .Take(5)
                .ToListAsync();

            return View();
        }

        // Add this method to be called when any of the counts change
        [HttpPost]
        public async Task<IActionResult> RefreshDashboardStats()
        {
            await UpdateDashboardStats();
            return Ok();
        }

        #region User Account Management

        // List pending LGU applications
        public async Task<IActionResult> PendingLGUApplications()
        {
            var pendingLGUs = await _context.Users
                .OfType<LGUUser>()
                .Where(u => !u.IsApproved)
                .ToListAsync();
                
            ViewBag.Title = "Pending LGU/SLU Applications";
            return View(pendingLGUs);
        }

        // View LGU application details
        public async Task<IActionResult> LGUApplicationDetails(string id)
        {
            var lguUser = await _context.Users
                .OfType<LGUUser>()
                .FirstOrDefaultAsync(u => u.Id == id);
                
            if (lguUser == null)
            {
                return NotFound();
            }
            
            ViewBag.Title = "LGU/SLU Application Details";
            return View(lguUser);
        }

        // Approve LGU application
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveLGU(string id)
        {
            var lguUser = await _context.Users
                .OfType<LGUUser>()
                .FirstOrDefaultAsync(u => u.Id == id);
                
            if (lguUser == null)
            {
                return NotFound();
            }
            
            lguUser.IsApproved = true;
            await _context.SaveChangesAsync();

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify clients about the LGU application update
            await _hubContext.Clients.All.SendAsync("LGUApplicationUpdated", id, true);
            
            TempData["SuccessMessage"] = "LGU/SLU application has been approved successfully.";
            return RedirectToAction(nameof(PendingLGUApplications));
        }

        // Reject LGU application
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectLGU(string id, string rejectionReason)
        {
            var lguUser = await _context.Users
                .OfType<LGUUser>()
                .FirstOrDefaultAsync(u => u.Id == id);
                
            if (lguUser == null)
            {
                return NotFound();
            }
            
            // Send rejection email
            try
            {
                await _emailSender.SendEmailAsync(
                    lguUser.Email,
                    "THYNK - LGU/SLU Application Rejected",
                    $"Dear {lguUser.FirstName},<br><br>We regret to inform you that your LGU/SLU application has been rejected.<br><br>" +
                    $"Reason: {rejectionReason}<br><br>" +
                    "If you believe this is an error or would like to reapply, please contact our support team.<br><br>" +
                    "Thank you for your interest in THYNK.<br><br>Best regards,<br>THYNK Administration Team"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send rejection email: {ex.Message}");
            }

            // Delete the user account
            _context.Users.Remove(lguUser);
            await _context.SaveChangesAsync();

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify clients about the LGU application update
            await _hubContext.Clients.All.SendAsync("LGUApplicationUpdated", id, false);
            
            TempData["SuccessMessage"] = "LGU/SLU application has been rejected and the account has been deleted.";
            return RedirectToAction(nameof(PendingLGUApplications));
        }

        // Manage users
        public async Task<IActionResult> ManageUsers(string search = "", string role = "", string status = "", string sortBy = "date", string sortOrder = "desc")
        {
            IQueryable<ApplicationUser> query = _context.Users;

            // Apply search filter if provided
            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                query = query.Where(u => 
                    u.Email.ToLower().Contains(search) ||
                    u.FirstName.ToLower().Contains(search) ||
                    u.LastName.ToLower().Contains(search) ||
                    (u is LGUUser && ((LGUUser)u).OrganizationName.ToLower().Contains(search))
                );
            }

            // Apply role filter if provided
            if (!string.IsNullOrEmpty(role))
            {
                if (role == "LGU")
                {
                    query = query.OfType<LGUUser>();
                }
                else if (role == "Admin")
                {
                    query = query.Where(u => u.UserRole == UserRoleType.Admin);
                }
                else if (role == "Community")
                {
                    query = query.Where(u => u.UserRole == UserRoleType.Community);
                }
            }

            // Apply status filter
            if (!string.IsNullOrEmpty(status))
            {
                switch (status.ToLower())
                {
                    case "active":
                        query = query.Where(u => u.LockoutEnd == null || u.LockoutEnd < DateTimeOffset.Now);
                        break;
                    case "deactivated":
                        query = query.Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.Now);
                        break;
                    case "pending":
                        query = query.OfType<LGUUser>().Where(u => !u.IsApproved);
                        break;
                }
            }

            // Apply sorting
            switch (sortBy.ToLower())
            {
                case "name":
                    query = sortOrder.ToLower() == "asc" 
                        ? query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                        : query.OrderByDescending(u => u.FirstName).ThenByDescending(u => u.LastName);
                    break;
                case "email":
                    query = sortOrder.ToLower() == "asc" 
                        ? query.OrderBy(u => u.Email)
                        : query.OrderByDescending(u => u.Email);
                    break;
                case "role":
                    query = sortOrder.ToLower() == "asc" 
                        ? query.OrderBy(u => u.UserRole)
                        : query.OrderByDescending(u => u.UserRole);
                    break;
                default: // date
                    query = sortOrder.ToLower() == "asc" 
                        ? query.OrderBy(u => u.DateCreated)
                        : query.OrderByDescending(u => u.DateCreated);
                    break;
            }

            // Store current filters in ViewBag for the view
            ViewBag.CurrentSearch = search;
            ViewBag.CurrentRole = role;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentSortBy = sortBy;
            ViewBag.CurrentSortOrder = sortOrder;

            var users = await query.ToListAsync();
            return View(users);
        }

        // Deactivate user
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            
            if (user == null)
            {
                return NotFound();
            }
            
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.Now.AddYears(100); // Effectively permanent
            
            await _userManager.UpdateAsync(user);
            
            TempData["SuccessMessage"] = "User has been deactivated successfully.";
            return RedirectToAction(nameof(ManageUsers));
        }

        // Reactivate user
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReactivateUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            
            if (user == null)
            {
                return NotFound();
            }
            
            user.LockoutEnd = null;
            
            await _userManager.UpdateAsync(user);
            
            TempData["SuccessMessage"] = "User has been reactivated successfully.";
            return RedirectToAction(nameof(ManageUsers));
        }

        #endregion

        #region Incident Report Oversight

        // Redirect Reports to IncidentReports
        public IActionResult Reports()
        {
            return RedirectToAction("IncidentReports");
        }

        // View all reports
        public async Task<IActionResult> IncidentReports(ReportStatus? status = null, string severity = null, DisasterType? type = null, string search = null)
        {
            IQueryable<DisasterReport> reportsQuery = _context.DisasterReports;
            
            if (status.HasValue)
            {
                reportsQuery = reportsQuery.Where(r => r.Status == status.Value);
            }

            if (type.HasValue)
            {
                reportsQuery = reportsQuery.Where(r => r.Type == type.Value);
            }
            
            // Handle search
            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                // Try to parse the search term as a disaster type
                if (Enum.TryParse<DisasterType>(search, true, out var disasterType))
                {
                    reportsQuery = reportsQuery.Where(r => r.Type == disasterType);
                }
                else
                {
                    // If not a valid disaster type, return no results
                    reportsQuery = reportsQuery.Where(r => false);
                }
            }
            
            // Special handling for InProgress reports with severity filtering
            if (status == ReportStatus.InProgress)
            {
                if (severity != null)
                {
                    if (Enum.TryParse<SeverityLevel>(severity, out var severityLevel))
                    {
                        reportsQuery = reportsQuery.Where(r => r.Severity == severityLevel);
                    }
                }
                
                // Order by severity (Critical first, then High, Medium, Low)
                reportsQuery = reportsQuery.OrderBy(r => r.Severity == SeverityLevel.Critical ? 0 :
                                              r.Severity == SeverityLevel.High ? 1 :
                                              r.Severity == SeverityLevel.Medium ? 2 : 3)
                                        .ThenByDescending(r => r.DateReported);
            }
            else
            {
                // For other statuses, use the default ordering
                reportsQuery = reportsQuery.OrderByDescending(r => r.DateReported);
            }
            
            var reports = await reportsQuery.ToListAsync();
                
            // Get available LGU/SLU users for assignment
            ViewBag.AvailableUsers = await _context.Users
                .OfType<LGUUser>()
                .Where(u => u.IsApproved)
                .ToListAsync();
                
            ViewBag.CurrentFilter = status;
            ViewBag.CurrentSeverity = severity;
            ViewBag.CurrentType = type;
            ViewBag.CurrentSearch = search;
            return View(reports);
        }

        // View report details
        public async Task<IActionResult> ReportDetails(int id)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
                .Include(r => r.AssignedTo)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (report == null)
            {
                return NotFound();
            }
            
            // Get LGU users for assignment dropdown
            ViewBag.LGUUsers = await _context.Users
                .OfType<LGUUser>()
                .Where(u => u.IsApproved)
                .ToListAsync();
                
            return View(report);
        }

        // Helper to format report for SignalR
        private object ToRecentReportDto(DisasterReport report)
        {
            return new {
                Id = report.Id,
                Title = report.Title,
                Type = report.Type.ToString(),
                Barangay = report.Barangay,
                City = report.City,
                DateReported = report.DateReported.ToString("MMM dd, yyyy HH:mm"),
                Status = report.Status.ToString()
            };
        }

        // Verify incident report
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyReport(int id)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (report == null)
            {
                return NotFound();
            }

            report.Status = ReportStatus.Verified;
            report.DateVerified = GetPhilippineTime();
            await _context.SaveChangesAsync();

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify admin clients about the report update
            await _hubContext.Clients.All.SendAsync("ReportUpdated", id, "Verified");

            // Send real-time update for Recent Reports in admin dashboard
            await _hubContext.Clients.All.SendAsync("RefreshRecentReports");

            // Send notification to the report owner
            if (report.User != null)
            {
                // Create a notification
                var notification = new UserNotification
                {
                    UserId = report.UserId,
                    Title = "Report Verified",
                    Message = $"Your report '{report.Title}' has been verified and is now being processed.",
                    NotificationType = "success",
                    RelatedEntityId = report.Id,
                    RelatedEntityType = "Report",
                    IsRead = false,
                    CreatedAt = GetPhilippineTime()
                };

                _context.UserNotifications.Add(notification);
                await _context.SaveChangesAsync();

                // Push notification to user's browser
                await _hubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("NotificationReceived", notification);

                // Send report status update notification
                await _communityHubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("ReportStatusUpdated", report.Id, "Verified", report.Title);
            }

            // Return to reports list
            return RedirectToAction("IncidentReports");
        }

        // Update report status
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReportStatus(int id, ReportStatus status, string? assignedTo)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null)
            {
                return NotFound();
            }

            report.Status = status;
            string notificationTitle = "";
            string notificationMessage = "";
            string notificationType = "info";

            // Set assigned user if provided
            if (!string.IsNullOrEmpty(assignedTo))
            {
                report.AssignedToId = assignedTo;
                report.AssignedAt = GetPhilippineTime();
                notificationTitle = "Report Assigned";
                notificationMessage = $"Your report '{report.Title}' has been assigned to local authorities.";
                notificationType = "info";
            }

            // Update timestamp and notification based on status
            switch (status)
            {
                case ReportStatus.InProgress:
                    report.DateInProgress = GetPhilippineTime();
                    notificationTitle = "Report In Progress";
                    notificationMessage = $"Your report '{report.Title}' is now being addressed by local authorities.";
                    notificationType = "info";
                    break;

                case ReportStatus.Resolved:
                    report.ResolvedAt = GetPhilippineTime();
                    notificationTitle = "Report Resolved";
                    notificationMessage = $"Your report '{report.Title}' has been successfully resolved.";
                    notificationType = "success";
                    break;

                case ReportStatus.Verified:
                    report.DateVerified = GetPhilippineTime();
                    notificationTitle = "Report Verified";
                    notificationMessage = $"Your report '{report.Title}' has been verified and is now being processed.";
                    notificationType = "success";
                    break;
            }

            // Create notification if we have a title and message
            if (!string.IsNullOrEmpty(notificationTitle) && report.User != null)
            {
                var notification = new UserNotification
                {
                    UserId = report.UserId,
                    Title = notificationTitle,
                    Message = notificationMessage,
                    NotificationType = notificationType,
                    RelatedEntityId = report.Id,
                    RelatedEntityType = "Report",
                    IsRead = false,
                    CreatedAt = GetPhilippineTime()
                };

                _context.UserNotifications.Add(notification);
                await _context.SaveChangesAsync();

                // Push notification to user's browser
                await _hubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("NotificationReceived", notification);

                // Send report status update notification
                await _communityHubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("ReportStatusUpdated", report.Id, status.ToString(), report.Title);
            }

            await _context.SaveChangesAsync();

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify admin clients about the report update
            await _hubContext.Clients.All.SendAsync("ReportUpdated", id, status.ToString());

            // Send real-time update for Recent Reports
            await _hubContext.Clients.All.SendAsync("RefreshRecentReports");

            return RedirectToAction("IncidentReports");
        }

        // Resolve reported incident
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

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify admin clients about the report update
            await _hubContext.Clients.All.SendAsync("ReportUpdated", id, "Resolved");

            // Send real-time update for Recent Reports
            await _hubContext.Clients.All.SendAsync("RefreshRecentReports");

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

                // Push notification to user's browser
                await _hubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("NotificationReceived", notification);

                // Send report status update notification
                await _communityHubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("ReportStatusUpdated", report.Id, "Resolved", report.Title);
            }

            // Return to reports list
            return RedirectToAction("IncidentReports");
        }

        // Decline incident report
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclineReport(int id, string declineReason)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (report == null)
            {
                return NotFound();
            }
            
            report.Status = ReportStatus.Declined;
            await _context.SaveChangesAsync();

            // Send notification email
            if (report.User != null)
            {
                try
                {
                    await _emailSender.SendEmailAsync(
                        report.User.Email,
                        "THYNK - Incident Report Declined",
                        $"Dear {report.User.FirstName},<br><br>Your incident report titled '{report.Title}' has been declined.<br><br>" +
                        $"Reason: {declineReason}<br><br>" +
                        "If you believe this is an error or would like to submit a new report, please contact our support team.<br><br>" +
                        "Thank you for your contribution to THYNK.<br><br>Best regards,<br>THYNK Administration Team"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to send decline notification email: {ex.Message}");
                }
            }

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify clients about the report update
            await _hubContext.Clients.All.SendAsync("ReportUpdated", id, "Declined");

            // Send real-time update for Recent Reports
            await _hubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));

            TempData["SuccessMessage"] = "Report has been declined and the reporter has been notified.";
            return RedirectToAction(nameof(ReportDetails), new { id });
        }

        // Assign report to LGU
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignReport(int id, string lguId)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (report == null)
            {
                return NotFound();
            }
            
            var lguUser = await _context.Users
                .OfType<LGUUser>()
                .FirstOrDefaultAsync(u => u.Id == lguId);
                
            if (lguUser == null)
            {
                return NotFound();
            }
            
            report.AssignedToId = lguId;
            report.Status = ReportStatus.InProgress;
            report.DateInProgress = GetPhilippineTime();
            report.AssignedAt = GetPhilippineTime();
            await _context.SaveChangesAsync();

            // Create notification for the report owner
            if (report.User != null)
            {
                var ownerNotification = new UserNotification
                {
                    UserId = report.UserId,
                    Title = "Report Assigned",
                    Message = $"Your report '{report.Title}' has been assigned to {lguUser.OrganizationName} and is now being processed.",
                    NotificationType = "info",
                    RelatedEntityId = report.Id,
                    RelatedEntityType = "Report",
                    IsRead = false,
                    CreatedAt = GetPhilippineTime()
                };

                _context.UserNotifications.Add(ownerNotification);
                await _context.SaveChangesAsync();

                // Send real-time notification to report owner
                await _communityHubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("NotificationReceived", ownerNotification);
                await _communityHubContext.Clients.Group($"user_{report.UserId}")
                    .SendAsync("ReportStatusUpdated", report.Id, "InProgress", report.Title);
            }

            // Create notification for the LGU user
            var lguNotification = new UserNotification
            {
                UserId = lguId,
                Title = "New Report Assignment",
                Message = $"You have been assigned to handle the report '{report.Title}'.",
                NotificationType = "info",
                RelatedEntityId = report.Id,
                RelatedEntityType = "Report",
                IsRead = false,
                CreatedAt = GetPhilippineTime()
            };

            _context.UserNotifications.Add(lguNotification);
            await _context.SaveChangesAsync();

            // Send real-time notification to LGU user
            await _communityHubContext.Clients.Group($"user_{lguId}")
                .SendAsync("NotificationReceived", lguNotification);

            // Notify LGU about the new report assignment using the new hub method
            await _communityHubContext.Clients.Group($"user_{lguId}")
                .SendAsync("NewReportAssigned", report);

            // Send email notification to LGU
            try
            {
                await _emailSender.SendEmailAsync(
                    lguUser.Email,
                    "THYNK - New Incident Report Assigned",
                    $"Dear {lguUser.FirstName},<br><br>A new incident report has been assigned to your organization.<br><br>" +
                    $"Title: {report.Title}<br>" +
                    $"Type: {report.Type}<br>" +
                    $"Location: {report.Barangay}, {report.City}<br><br>" +
                    "Please review and take appropriate action.<br><br>" +
                    "Best regards,<br>THYNK Administration Team"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send assignment notification email: {ex.Message}");
            }

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify all clients about the report update
            await _hubContext.Clients.All.SendAsync("ReportUpdated", id, "InProgress");
            await _hubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));

            TempData["SuccessMessage"] = "Report has been assigned to the LGU/SLU successfully.";
            return RedirectToAction(nameof(IncidentReports));
        }

        #endregion

        #region Community Post Management

        // View pending posts
        public async Task<IActionResult> PendingPosts()
        {
            var pendingPosts = await _context.CommunityUpdates
                .Include(c => c.User)
                .Where(c => c.ModerationStatus == ModerationStatus.Pending)
                .OrderByDescending(c => c.DatePosted)
                .ToListAsync();

            return View("ManagePosts", pendingPosts);
        }

        // Manage all community posts
        public async Task<IActionResult> ManagePosts(string ModerationStatus, string search)
        {
            var query = _context.CommunityUpdates
                .Include(c => c.User)
                .AsQueryable();

            // Apply moderation status filter
            if (!string.IsNullOrEmpty(ModerationStatus))
            {
                if (Enum.TryParse<ModerationStatus>(ModerationStatus, out var status))
                {
                    query = query.Where(c => c.ModerationStatus == status);
                }
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                query = query.Where(c =>
                    c.Content.ToLower().Contains(search) ||
                    c.Location.ToLower().Contains(search) ||
                    (c.User != null && (
                        c.User.FirstName.ToLower().Contains(search) ||
                        c.User.LastName.ToLower().Contains(search) ||
                        (c.User is LGUUser && ((LGUUser)c.User).OrganizationName.ToLower().Contains(search))
                    ))
                );
            }

            // Order by date posted
            query = query.OrderByDescending(c => c.DatePosted);

            ViewBag.CurrentFilter = ModerationStatus;
            ViewBag.CurrentSearch = search;

            return View(await query.ToListAsync());
        }

        // Moderate a post (approve/reject)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ModeratePost(int postId, string action)
        {
            var post = await _context.CommunityUpdates
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == postId);

            if (post == null)
            {
                return NotFound();
            }

            if (action == "approve")
            {
                post.ModerationStatus = ModerationStatus.Approved;
                await _context.SaveChangesAsync();

                // Update dashboard stats
                await UpdateDashboardStats();

                // Notify clients about the post update
                await _hubContext.Clients.All.SendAsync("PostUpdated", postId, true);

                // Send notification to the post author
                if (post.User != null)
                {
                    await _communityHubContext.Clients.Group($"user_{post.UserId}")
                        .SendAsync("PostModerationUpdate", new {
                            postId = post.Id,
                            isApproved = true,
                            timestamp = GetPhilippineTime()
                        });

                    // Create a notification record
                    var notification = new UserNotification
                    {
                        UserId = post.UserId,
                        Title = "Post Approved",
                        Message = "Your community post has been approved and is now visible in the community feed.",
                        NotificationType = "success",
                        RelatedEntityId = post.Id,
                        RelatedEntityType = "CommunityPost",
                        CreatedAt = GetPhilippineTime(),
                        IsRead = false
                    };
                    _context.UserNotifications.Add(notification);
                    await _context.SaveChangesAsync();

                    // Send real-time notification
                    await _communityHubContext.Clients.Group($"user_{post.UserId}")
                        .SendAsync("NotificationReceived", new {
                            id = notification.Id,
                            title = notification.Title,
                            message = notification.Message,
                            type = notification.NotificationType,
                            createdAt = notification.CreatedAt.ToLocalTime()
                        });
                }

                TempData["SuccessMessage"] = "Post has been approved and is now visible in the community feed.";
            }
            else if (action == "reject")
            {
                var rejectionReason = Request.Form["rejectionReason"].ToString();
                
                // Send rejection notification
                if (post.User != null)
                {
                    // Send real-time notification
                    await _communityHubContext.Clients.Group($"user_{post.UserId}")
                        .SendAsync("PostModerationUpdate", new {
                            postId = post.Id,
                            isApproved = false,
                            reason = rejectionReason,
                            timestamp = GetPhilippineTime()
                        });

                    // Create a notification record
                    var notification = new UserNotification
                    {
                        UserId = post.UserId,
                        Title = "Post Rejected",
                        Message = $"Your community post has been rejected. Reason: {rejectionReason}",
                        NotificationType = "warning",
                        RelatedEntityId = post.Id,
                        RelatedEntityType = "CommunityPost",
                        CreatedAt = GetPhilippineTime(),
                        IsRead = false
                    };
                    _context.UserNotifications.Add(notification);
                    await _context.SaveChangesAsync();

                    // Send real-time notification
                    await _communityHubContext.Clients.Group($"user_{post.UserId}")
                        .SendAsync("NotificationReceived", new {
                            id = notification.Id,
                            title = notification.Title,
                            message = notification.Message,
                            type = notification.NotificationType,
                            createdAt = notification.CreatedAt.ToLocalTime()
                        });

                    try
                    {
                        await _emailSender.SendEmailAsync(
                            post.User.Email,
                            "THYNK - Community Post Rejected",
                            $"Dear {post.User.FirstName},<br><br>Your community post has been rejected.<br><br>" +
                            $"Reason: {rejectionReason}<br><br>" +
                            "If you believe this is an error or would like to submit a new post, please contact our support team.<br><br>" +
                            "Thank you for your contribution to THYNK.<br><br>Best regards,<br>THYNK Administration Team"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to send rejection notification email: {ex.Message}");
                    }
                }

                _context.CommunityUpdates.Remove(post);
                await _context.SaveChangesAsync();

                // Update dashboard stats
                await UpdateDashboardStats();

                // Notify clients about the post update
                await _hubContext.Clients.All.SendAsync("PostUpdated", postId, false);

                TempData["SuccessMessage"] = "Post has been rejected and removed from the system.";
            }

            return RedirectToAction(nameof(PendingPosts));
        }

        #endregion

        #region Alert Management

        // Create alert form
        public IActionResult CreateAlert()
        {
            return View();
        }

        // Submit new alert
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAlert(Alert alert)
        {
            if (ModelState.IsValid)
            {
                // Set the current user ID as the issuer
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                alert.IssuedByUserId = userId;
                alert.DateIssued = DateTime.Now;
                alert.IsActive = true;
                
                _context.Add(alert);
                await _context.SaveChangesAsync();
                
                // Send SMS notifications
                try
                {
                    await _alertNotificationService.SendAlertNotifications(alert);
                    _logger.LogInformation($"SMS notifications sent for alert {alert.Id}");
                }
                catch (Exception ex)
                {
                    // Log but don't fail the operation if SMS sending fails
                    _logger.LogError(ex, "Error sending SMS notifications for alert: {Message}", ex.Message);
                }
                
                TempData["SuccessMessage"] = "Alert has been created successfully and SMS notifications have been sent.";
                return RedirectToAction(nameof(ManageAlerts));
            }
            
            return View(alert);
        }

        // Manage all alerts
        public async Task<IActionResult> ManageAlerts()
        {
            var alerts = await _context.Alerts
                .OrderByDescending(a => a.DateIssued)
                .ToListAsync();
                
            return View(alerts);
        }

        // Deactivate alert
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateAlert(int id)
        {
            var alert = await _context.Alerts
                .FirstOrDefaultAsync(a => a.Id == id);
                
            if (alert == null)
            {
                return NotFound();
            }
            
            alert.IsActive = false;
            alert.ExpiresAt = GetPhilippineTime();
            
            _context.Update(alert);
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Alert has been deactivated successfully.";
            return RedirectToAction(nameof(ManageAlerts));
        }

        #endregion

        #region System Logs & Audit

        // View system logs
        public async Task<IActionResult> SystemLogs(DateTime? startDate, DateTime? endDate)
        {
            // In a real system, you'd implement a proper logging system
            // This is a placeholder - would need a SystemLog model & table
            
            // For demo purposes, we'll return an empty list
            var logs = new List<object>();
            return View(logs);
        }

        #endregion

        // View community feed
       
        [HttpGet]
        public async Task<IActionResult> GetRecentReports()
        {
            var recentReports = await _context.DisasterReports
                .OrderByDescending(r => r.DateReported)
                .Take(5)
                .Select(r => new {
                    Id = r.Id,
                    Title = r.Title,
                    Type = r.Type.ToString(),
                    Barangay = r.Barangay,
                    City = r.City,
                    DateReported = r.DateReported.ToString("MMM dd, yyyy HH:mm"),
                    Status = r.Status.ToString()
                })
                .ToListAsync();

            return Json(recentReports);
        }

        [HttpGet]
        public async Task<IActionResult> GetPendingReportsCount()
        {
            var pendingReportsCount = await _context.DisasterReports
                .CountAsync(r => r.Status == ReportStatus.Pending);
            return Json(new { count = pendingReportsCount });
        }

        // GET: List pending educational resources
        public async Task<IActionResult> PendingResources()
        {
            var pendingResources = await _context.EducationalResources
                .Include(r => r.CreatedBy)
                .Where(r => r.ApprovalStatus == ApprovalStatus.Pending)
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();
                
            return View(pendingResources);
        }
        
        // GET: Resource details for review
        public async Task<IActionResult> ReviewResource(int id)
        {
            var resource = await _context.EducationalResources
                .Include(r => r.CreatedBy)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            return View(resource);
        }
        
        // POST: Approve resource
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveResource(int id)
        {
            var resource = await _context.EducationalResources
                .Include(r => r.CreatedBy)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            // Update status
            resource.ApprovalStatus = ApprovalStatus.Approved;
            resource.ApprovedDate = GetPhilippineTime();
            resource.RejectionReason = null;
            
            // Save changes
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Resource has been approved and is now available in the library.";
            return RedirectToAction(nameof(PendingResources));
        }
        
        // POST: Reject resource
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectResource(int id, string rejectionReason)
        {
            var resource = await _context.EducationalResources
                .Include(r => r.CreatedBy)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            // Update status
            resource.ApprovalStatus = ApprovalStatus.Rejected;
            resource.RejectionReason = rejectionReason;
            
            // Save changes
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Resource has been rejected and the creator has been notified.";
            return RedirectToAction(nameof(PendingResources));
        }
        
        // GET: All resources
        public async Task<IActionResult> AllResources()
        {
            var resources = await _context.EducationalResources
                .Include(r => r.CreatedBy)
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();
                
            return View(resources);
        }
    }
} 