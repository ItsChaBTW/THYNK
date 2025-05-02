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
using Microsoft.AspNetCore.Hosting;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace THYNK.Controllers
{
    [Authorize(Roles = "Community")]
    public class CommunityController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public CommunityController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
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
        public async Task<IActionResult> SubmitReport(DisasterReport report, IFormFile photo)
        {
            if (ModelState.IsValid)
            {
                report.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                report.DateReported = DateTime.Now;
                report.Status = ReportStatus.Pending;
                
                // Handle photo upload if provided
                if (photo != null && photo.Length > 0)
                {
                    // Create folder if it doesn't exist
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "reports");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    
                    // Generate unique filename
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(photo.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    
                    // Save file to disk
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await photo.CopyToAsync(fileStream);
                    }
                    
                    // Set ImageUrl property
                    report.ImageUrl = "/uploads/reports/" + uniqueFileName;
                }
                else
                {
                    // Set default image if no photo provided
                    report.ImageUrl = "/images/default-report.jpg";
                }
                
                // Ensure any empty location fields are set to a default value
                report.Purok = string.IsNullOrEmpty(report.Purok) ? "Unknown" : report.Purok;
                report.Barangay = string.IsNullOrEmpty(report.Barangay) ? "Unknown" : report.Barangay;
                report.City = string.IsNullOrEmpty(report.City) ? "Unknown" : report.City;
                report.Country = string.IsNullOrEmpty(report.Country) ? "Philippines" : report.Country;
                
                // Set empty AdditionalInfo to an empty string rather than null
                report.AdditionalInfo = report.AdditionalInfo ?? string.Empty;
                
                _context.Add(report);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Your incident report has been successfully submitted and is pending review.";
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
                    r.DateReported,
                    r.Location,
                    r.Barangay,
                    r.City
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