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

        public AdminController(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
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
            
            // Send approval email
            await _emailSender.SendEmailAsync(
                lguUser.Email,
                "THYNK - LGU/SLU Account Approved",
                $"Dear {lguUser.FirstName},<br><br>Your LGU/SLU account for THYNK has been approved. You can now log in and access the LGU/SLU features.<br><br>Thank you,<br>THYNK Administration Team"
            );
            
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
        public async Task<IActionResult> ManageUsers()
        {
            var users = await _context.Users.ToListAsync();
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
                
            ViewBag.CurrentFilter = status;
            return View(reports);
        }

        // View report details
        public async Task<IActionResult> ReportDetails(int id)
        {
            var report = await _context.DisasterReports
                .Include(r => r.User)
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
            var report = await _context.DisasterReports
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (report == null)
            {
                return NotFound();
            }
            
            report.Status = ReportStatus.Verified;
            _context.Update(report);
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Report has been verified successfully.";
            return RedirectToAction(nameof(ReportDetails), new { id = id });
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
        public async Task<IActionResult> AssignReportToLGU(int reportId, string lguUserId)
        {
            var report = await _context.DisasterReports
                .FirstOrDefaultAsync(r => r.Id == reportId);
                
            var lguUser = await _context.Users
                .OfType<LGUUser>()
                .FirstOrDefaultAsync(u => u.Id == lguUserId);
                
            if (report == null || lguUser == null)
            {
                return NotFound();
            }
            
            // Here you would create an assignment record or update the report with assignment info
            // For simplicity, we'll just update the status to InProgress
            report.Status = ReportStatus.InProgress;
            report.AssignedToId = lguUserId;
            report.AssignedAt = DateTime.Now;
            
            _context.Update(report);
            await _context.SaveChangesAsync();
            
            // Send notification to LGU
            await _emailSender.SendEmailAsync(
                lguUser.Email,
                "THYNK - New Incident Report Assignment",
                $"Dear {lguUser.FirstName},<br><br>A new incident report titled '{report.Title}' has been assigned to you for action.<br><br>Please log in to your LGU dashboard to view the details and take appropriate action.<br><br>Thank you,<br>THYNK Administration Team"
            );
            
            TempData["SuccessMessage"] = "Report has been assigned successfully.";
            return RedirectToAction(nameof(ReportDetails), new { id = reportId });
        }

        #endregion

        #region Content Moderation

        // View pending community posts
        public async Task<IActionResult> PendingPosts()
        {
            var pendingPosts = await _context.CommunityUpdates
                .Where(c => c.ModerationStatus == ModerationStatus.Pending)
                .Include(c => c.User)
                .OrderByDescending(c => c.DatePosted)
                .ToListAsync();
                
            return View(pendingPosts);
        }

        // View post details
        public async Task<IActionResult> PostDetails(int id)
        {
            var post = await _context.CommunityUpdates
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);
                
            if (post == null)
            {
                return NotFound();
            }
            
            return View(post);
        }

        // Approve community post
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePost(int id)
        {
            var post = await _context.CommunityUpdates
                .FirstOrDefaultAsync(c => c.Id == id);
                
            if (post == null)
            {
                return NotFound();
            }
            
            post.ModerationStatus = ModerationStatus.Approved;
            _context.Update(post);
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Community post has been approved successfully.";
            return RedirectToAction(nameof(PendingPosts));
        }

        // Reject community post
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPost(int id, string reason)
        {
            var post = await _context.CommunityUpdates
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);
                
            if (post == null)
            {
                return NotFound();
            }
            
            post.ModerationStatus = ModerationStatus.Rejected;
            _context.Update(post);
            await _context.SaveChangesAsync();
            
            // Send email notification to user
            if (post.User != null)
            {
                await _emailSender.SendEmailAsync(
                    post.User.Email,
                    "THYNK - Community Post Status Update",
                    $"Dear User,<br><br>Your community post has been reviewed and could not be approved for public visibility.<br><br>Reason: {reason}<br><br>Please review our community guidelines for acceptable content.<br><br>Thank you,<br>THYNK Administration Team"
                );
            }
            
            TempData["SuccessMessage"] = "Community post has been rejected successfully.";
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
    }
} 