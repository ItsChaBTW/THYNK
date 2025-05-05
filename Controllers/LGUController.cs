using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using THYNK.Data;
using THYNK.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;

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

        // Manage disaster reports
        public async Task<IActionResult> ManageReports()
        {
            var reports = await _context.DisasterReports
                .OrderByDescending(r => r.DateReported)
                .ToListAsync();
                
            return View(reports);
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
        public async Task<IActionResult> ManageAlerts()
        {
            var alerts = await _context.Alerts
                .OrderByDescending(a => a.DateIssued)
                .ToListAsync();
                
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