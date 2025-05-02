using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using THYNK.Data;
using THYNK.Models;

namespace THYNK.Controllers
{
    [Authorize(Roles = "Community")]
    public class CommunityController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CommunityController(ApplicationDbContext context)
        {
            _context = context;
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
                
            // Get recent alerts
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
        
        // Submit disaster report
        public IActionResult SubmitReport()
        {
            return View();
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReport(DisasterReport report)
        {
            if (ModelState.IsValid)
            {
                report.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                report.DateReported = DateTime.Now;
                report.Status = ReportStatus.Pending;
                
                _context.Add(report);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Dashboard));
            }
            return View(report);
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
        
        // Post community update
        public IActionResult PostUpdate()
        {
            return View();
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostUpdate(CommunityUpdate update)
        {
            if (ModelState.IsValid)
            {
                update.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                update.DatePosted = DateTime.Now;
                update.ModerationStatus = ModerationStatus.Pending;
                
                _context.Add(update);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(CommunityFeed));
            }
            return View(update);
        }
        
        // Access educational resources
        public async Task<IActionResult> EducationalResources()
        {
            var resources = await _context.EducationalResources
                .OrderBy(r => r.Title)
                .ToListAsync();
                
            return View(resources);
        }
        
        // View educational resource details
        public async Task<IActionResult> ResourceDetails(int id)
        {
            var resource = await _context.EducationalResources
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            return View(resource);
        }
        
        // View alerts
        public async Task<IActionResult> Alerts()
        {
            var activeAlerts = await _context.Alerts
                .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now))
                .OrderByDescending(a => a.DateIssued)
                .ToListAsync();
                
            return View(activeAlerts);
        }
        
        // View incident map
        public IActionResult IncidentMap()
        {
            return View();
        }
        
        // API endpoint to get map data
        [HttpGet]
        public async Task<JsonResult> GetMapData()
        {
            var reports = await _context.DisasterReports
                .Where(r => r.Status != ReportStatus.Declined)
                .Select(r => new {
                    r.Id,
                    r.Title,
                    r.Description,
                    r.Type,
                    r.Latitude,
                    r.Longitude,
                    r.Severity,
                    r.Status,
                    r.DateReported
                })
                .ToListAsync();
                
            return Json(reports);
        }
        
        // Chat support / FAQ
        public IActionResult Support()
        {
            return View();
        }
        
        // My reports (view user's own reports)
        public async Task<IActionResult> MyReports()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var reports = await _context.DisasterReports
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.DateReported)
                .ToListAsync();
                
            return View(reports);
        }
        
        // View report details
        public async Task<IActionResult> ReportDetails(int id)
        {
            var report = await _context.DisasterReports
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (report == null)
            {
                return NotFound();
            }
            
            return View(report);
        }
    }
} 