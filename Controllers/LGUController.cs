using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using THYNK.Data;
using THYNK.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authentication;

namespace THYNK.Controllers
{
    [Authorize]
    public class LGUController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LGUController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        public LGUController(ApplicationDbContext context, ILogger<LGUController> logger, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _logger = logger;
            _userManager = userManager;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
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
            
            // Get recent disaster reports
            var recentReports = await _context.DisasterReports
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
        public async Task<IActionResult> ManageReports(ReportStatus? status = null, string search = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            IQueryable<DisasterReport> reportsQuery = _context.DisasterReports;
            
            // First get reports assigned to this LGU/SLU user
            reportsQuery = reportsQuery.Where(r => r.AssignedToId == userId || r.Status == ReportStatus.InProgress || r.Status == ReportStatus.Verified);
            
            // Filter by status if provided
            if (status.HasValue)
            {
                reportsQuery = reportsQuery.Where(r => r.Status == status.Value);
            }
            
            // Filter by search text if provided
            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                reportsQuery = reportsQuery.Where(r => 
                    r.Title.ToLower().Contains(search) || 
                    r.Description.ToLower().Contains(search) || 
                    r.Location.ToLower().Contains(search));
            }
            
            // Get reports, ordered by date
            var reports = await reportsQuery
                .OrderByDescending(r => r.DateReported)
                .ToListAsync();
                
            ViewBag.CurrentFilter = status;
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
            
            return View(report);
        }
        
        // Update report status
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReportStatus(int id, int status, string notes)
        {
            var report = await _context.DisasterReports.FindAsync(id);
            
            if (report == null)
            {
                return NotFound();
            }
            
            // Update report status
            report.Status = (ReportStatus)status;
            
            // Add response log entry (if we had a ResponseLog table)
            // Instead we can store in the AdditionalInfo field for now as JSON
            var currentUser = await _userManager.GetUserAsync(User);
            string responderName = $"{currentUser.FirstName} {currentUser.LastName}";
            string responseNote = $"[{DateTime.Now:yyyy-MM-dd HH:mm}] Status updated to {report.Status} by {responderName}. Notes: {notes}";
            
            // Append to existing info with a separator
            if (!string.IsNullOrEmpty(report.AdditionalInfo))
            {
                report.AdditionalInfo += "\n\n---\n\n";
            }
            report.AdditionalInfo += responseNote;
            
            // If resolved, set the resolution date
            if (report.Status == ReportStatus.Resolved)
            {
                // We would have a ResolutionDate field, but for now we'll just use AdditionalInfo
                report.AdditionalInfo += $"\n\nResolved on: {DateTime.Now:yyyy-MM-dd HH:mm}";
            }
            
            _context.Update(report);
            await _context.SaveChangesAsync();
            
            // Notify the reporter if user info is available
            if (report.User != null && !string.IsNullOrEmpty(report.User.Email))
            {
                // This would use the email service in a real app
                _logger.LogInformation($"Would send email to {report.User.Email} about status update for report {report.Id}");
            }
            
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
        public async Task<IActionResult> CreateAlert(Alert alert)
        {
            if (ModelState.IsValid)
            {
                alert.DateIssued = DateTime.UtcNow;
                alert.IsActive = true;
                alert.IssuedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                _context.Alerts.Add(alert);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(ManageAlerts));
            }
            return View(alert);
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
            }
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
            var resources = await _context.EducationalResources
                .OrderBy(r => r.Title)
                .ToListAsync();
                
            return View(resources);
        }

        // View community feed
        public async Task<IActionResult> CommunityFeed()
        {
            var updates = await _context.CommunityUpdates
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

        // User profile
        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId);
                
            if (user == null)
            {
                return NotFound();
            }
            
            return View(user);
        }

        // API endpoint to get map data
        [HttpGet]
        public async Task<JsonResult> GetMapData()
        {
            var reports = await _context.DisasterReports
                .Select(r => new {
                    r.Id,
                    r.Title,
                    r.Type,
                    r.Severity,
                    r.Status,
                    r.Latitude,
                    r.Longitude,
                    r.DateReported
                })
                .ToListAsync();
                
            return Json(reports);
        }
    }
} 