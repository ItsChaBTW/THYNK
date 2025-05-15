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

        public AdminController(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            IWebHostEnvironment webHostEnvironment,
            ILogger<AdminController> logger,
            IHubContext<AdminHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
            _hubContext = hubContext;
        }

        private async Task UpdateDashboardStats()
        {
            var pendingLGUCount = await _context.Users.OfType<LGUUser>().CountAsync(u => !u.IsApproved);
            var pendingReportsCount = await _context.DisasterReports.CountAsync(r => r.Status == ReportStatus.Pending);
            var pendingPostsCount = await _context.CommunityUpdates.CountAsync(p => p.ModerationStatus == ModerationStatus.Pending);

            await _hubContext.Clients.All.SendAsync("ReceiveDashboardStats", pendingLGUCount, pendingReportsCount, pendingPostsCount);
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

        // View all reports
        public async Task<IActionResult> IncidentReports(ReportStatus? status = null)
        {
            IQueryable<DisasterReport> reportsQuery = _context.DisasterReports;
            
            if (status.HasValue)
            {
                reportsQuery = reportsQuery.Where(r => r.Status == status.Value);
            }
            
            var reports = await reportsQuery
                .OrderByDescending(r => r.DateReported)
                .ToListAsync();
                
            // Get available LGU/SLU users for assignment
            ViewBag.AvailableUsers = await _context.Users
                .OfType<LGUUser>()
                .Where(u => u.IsApproved)
                .ToListAsync();
                
            ViewBag.CurrentFilter = status;
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
            var report = await _context.DisasterReports.FindAsync(id);
            if (report == null)
            {
                return NotFound();
            }

            report.Status = ReportStatus.Verified;
            await _context.SaveChangesAsync();

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify clients about the report update
            await _hubContext.Clients.All.SendAsync("ReportUpdated", id, "Verified");

            // Send real-time update for Recent Reports
            await _hubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));

            TempData["SuccessMessage"] = "Report has been verified successfully.";
            return RedirectToAction(nameof(ReportDetails), new { id });
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
            await _context.SaveChangesAsync();

            // Send notification email
            if (lguUser != null)
            {
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
            }

            // Update dashboard stats
            await UpdateDashboardStats();

            // Notify clients about the report update
            await _hubContext.Clients.All.SendAsync("ReportUpdated", id, "InProgress");

            // Send real-time update for Recent Reports
            await _hubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));

            // Send notification to the assigned LGU
            await _hubContext.Clients.All.SendAsync("ReportAssigned", ToRecentReportDto(report));

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

                TempData["SuccessMessage"] = "Post has been approved and is now visible in the community feed.";
            }
            else if (action == "reject")
            {
                var rejectionReason = Request.Form["rejectionReason"].ToString();
                
                // Send rejection notification
                if (post.User != null)
                {
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
                alert.DateIssued = DateTime.Now;
                alert.IsActive = true;
                
                _context.Add(alert);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Alert has been created successfully.";
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
            alert.ExpiresAt = DateTime.Now;
            
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
    }
} 