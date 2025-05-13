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

        public AdminController(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            IWebHostEnvironment webHostEnvironment,
            ILogger<AdminController> logger)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        // Dashboard
        public async Task<IActionResult> Dashboard()
        {
            // Get counts for dashboard
            ViewBag.PendingLGUCount = await _context.Users
                .OfType<LGUUser>()
                .Where(u => !u.IsApproved)
                .CountAsync();
                
            ViewBag.PendingReportsCount = await _context.DisasterReports
                .Where(r => r.Status == ReportStatus.Pending)
                .CountAsync();
                
            ViewBag.PendingPostsCount = await _context.CommunityUpdates
                .Where(c => c.ModerationStatus == ModerationStatus.Pending)
                .CountAsync();
                
            ViewBag.ActiveUsersCount = await _context.Users
                .CountAsync();
                
            ViewBag.RecentReports = await _context.DisasterReports
                .OrderByDescending(r => r.DateReported)
                .Take(5)
                .ToListAsync();
                
            return View();
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
            _context.Update(lguUser);
            await _context.SaveChangesAsync();
            
            // Send approval email only if email is not null
            if (!string.IsNullOrEmpty(lguUser.Email))
            {
                try
                {
                    await _emailSender.SendEmailAsync(
                        lguUser.Email,
                        "THYNK - LGU/SLU Account Approved",
                        $"Dear {lguUser.FirstName},<br><br>Your LGU/SLU account for THYNK has been approved. You can now log in and access the LGU/SLU features.<br><br>Thank you,<br>THYNK Administration Team"
                    );
                }
                catch (Exception ex)
                {
                    // Log the error but don't fail the approval process
                    _logger.LogError($"Failed to send approval email to {lguUser.Email}: {ex.Message}");
                }
            }
            
            TempData["SuccessMessage"] = "LGU/SLU account has been approved successfully.";
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
            
            // Delete the user account
            _context.Users.Remove(lguUser);
            await _context.SaveChangesAsync();
            
            // Send rejection email
            await _emailSender.SendEmailAsync(
                lguUser.Email,
                "THYNK - LGU/SLU Account Application Rejected",
                $"Dear {lguUser.FirstName},<br><br>We regret to inform you that your LGU/SLU account application for THYNK has been rejected for the following reason:<br><br>{rejectionReason}<br><br>If you believe this is an error or would like to provide additional information, please contact our support team.<br><br>Thank you,<br>THYNK Administration Team"
            );
            
            TempData["SuccessMessage"] = "LGU/SLU account has been rejected and the user has been notified.";
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

        // Verify incident report
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyReport(int id)
        {
            try
            {
                var report = await _context.DisasterReports
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == id);
                    
                if (report == null)
                {
                    return NotFound();
                }

                if (report.Status != ReportStatus.Pending)
                {
                    TempData["ErrorMessage"] = "Only pending reports can be verified.";
                    return RedirectToAction(nameof(IncidentReports));
                }
                
                report.Status = ReportStatus.Verified;
                _context.Update(report);
                await _context.SaveChangesAsync();
                
                // Send email notification to user
                if (report.User != null && !string.IsNullOrEmpty(report.User.Email))
                {
                    try
                    {
                        await _emailSender.SendEmailAsync(
                            report.User.Email,
                            "THYNK - Incident Report Verified",
                            $"Dear {report.User.FirstName},<br><br>Your incident report titled '{report.Title}' has been verified by our team.<br><br>Thank you for helping keep our community safe.<br><br>THYNK Administration Team"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to send verification email: {ex.Message}");
                    }
                }
                
                TempData["SuccessMessage"] = "Report has been verified successfully.";
                return RedirectToAction(nameof(IncidentReports));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error verifying report: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while verifying the report.";
                return RedirectToAction(nameof(IncidentReports));
            }
        }

        // Decline incident report
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclineReport(int id, string reason)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (report == null)
            {
                return NotFound();
            }
            
            report.Status = ReportStatus.Declined;
            _context.Update(report);
            await _context.SaveChangesAsync();
            
            // Send email to user
            if (report.User != null)
            {
                await _emailSender.SendEmailAsync(
                    report.User.Email,
                    "THYNK - Incident Report Status Update",
                    $"Dear User,<br><br>Your incident report titled '{report.Title}' has been reviewed and unfortunately could not be verified.<br><br>Reason: {reason}<br><br>If you believe this is an error or would like to provide additional information, please submit a new report.<br><br>Thank you,<br>THYNK Administration Team"
                );
            }
            
            TempData["SuccessMessage"] = "Report has been declined successfully.";
            return RedirectToAction(nameof(ReportDetails), new { id = id });
        }

        // Assign report to LGU
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignReportToLGU(int reportId, string lguUserId, string assignmentNote)
        {
            try
            {
                var report = await _context.DisasterReports
                    .Include(r => r.User)
                    .FirstOrDefaultAsync(r => r.Id == reportId);
                    
                var lguUser = await _context.Users
                    .OfType<LGUUser>()
                    .FirstOrDefaultAsync(u => u.Id == lguUserId && u.IsApproved);
                    
                if (report == null || lguUser == null)
                {
                    return NotFound();
                }

                if (report.Status != ReportStatus.Verified)
                {
                    TempData["ErrorMessage"] = "Only verified reports can be assigned to LGU users.";
                    return RedirectToAction(nameof(ReportDetails), new { id = reportId });
                }
                
                report.Status = ReportStatus.InProgress;
                report.AssignedToId = lguUserId;
                report.AssignedAt = DateTime.Now;
                
                _context.Update(report);
                await _context.SaveChangesAsync();
                
                // Send notification to LGU
                if (!string.IsNullOrEmpty(lguUser.Email))
                {
                    try
                    {
                        await _emailSender.SendEmailAsync(
                            lguUser.Email,
                            "THYNK - New Incident Report Assignment",
                            $"Dear {lguUser.FirstName},<br><br>A new incident report titled '{report.Title}' has been assigned to you for action.<br><br>" +
                            $"Assignment Note: {assignmentNote}<br><br>" +
                            "Please log in to your LGU dashboard to view the details and take appropriate action.<br><br>Thank you,<br>THYNK Administration Team"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to send assignment email: {ex.Message}");
                    }
                }
                
                TempData["SuccessMessage"] = "Report has been assigned successfully.";
                return RedirectToAction(nameof(ReportDetails), new { id = reportId });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error assigning report: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while assigning the report.";
                return RedirectToAction(nameof(ReportDetails), new { id = reportId });
            }
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
                TempData["SuccessMessage"] = "Post has been approved successfully.";
            }
            else if (action == "reject")
            {
                post.ModerationStatus = ModerationStatus.Rejected;
                TempData["SuccessMessage"] = "Post has been rejected successfully.";

                // Send email notification to user
                if (post.User != null)
                {
                    await _emailSender.SendEmailAsync(
                        post.User.Email,
                        "THYNK - Community Post Status Update",
                        $"Dear User,<br><br>Your community post has been reviewed and could not be approved for public visibility.<br><br>Please review our community guidelines for acceptable content.<br><br>Thank you,<br>THYNK Administration Team"
                    );
                }
            }

            _context.Update(post);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManagePosts));
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
       
    }
} 