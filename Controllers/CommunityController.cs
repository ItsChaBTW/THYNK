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
using THYNK.Services;

namespace THYNK.Controllers
{
    [Authorize(Roles = "Community")]
    public class CommunityController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly PdfService _pdfService;

        public CommunityController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment, PdfService pdfService)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _pdfService = pdfService;
        }

        // Dashboard main page
        public async Task<IActionResult> Dashboard()
        {
            // Redirect to CommunityFeed instead of showing the dashboard
            return RedirectToAction(nameof(CommunityFeed));
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
            // Manual validation for required fields that might be missed by model validation
            if (string.IsNullOrEmpty(report.Title))
            {
                ModelState.AddModelError("Title", "Title is required");
            }
            
            if (string.IsNullOrEmpty(report.Description))
            {
                ModelState.AddModelError("Description", "Description is required");
            }
            
            if (string.IsNullOrEmpty(report.Location))
            {
                ModelState.AddModelError("Location", "Location is required");
            }

            // Validate photo is provided
            if (photo == null || photo.Length == 0)
            {
                ModelState.AddModelError("Photo", "A photo of the incident is required");
            }
            
            // Remove any validation errors for AdditionalInfo since it's optional
            ModelState.Remove("AdditionalInfo");
            
            // Debug: Add validation state information to TempData
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value.Errors.Count > 0)
                    .Select(x => new { Key = x.Key, Errors = x.Value.Errors.Select(e => e.ErrorMessage).ToList() })
                    .ToList();
                
                ViewBag.ValidationErrors = errors;
                TempData["ErrorMessage"] = "Form validation failed. Please check the highlighted fields.";
                return View(report);
            }
            
            try
            {
                // Debug: Make sure UserId is set
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    TempData["ErrorMessage"] = "User ID could not be determined. Please log in again.";
                    return View(report);
                }
                
                report.UserId = userId;
                report.DateReported = DateTime.Now;
                report.Status = ReportStatus.Pending;
                
                // Handle photo upload
                try
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
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = "Error uploading photo: " + ex.Message;
                    return View(report);
                }
                
                // Debug: Check if coordinates are set
                if (report.Latitude == 0 && report.Longitude == 0)
                {
                    TempData["WarningMessage"] = "Location coordinates appear to be missing. Please ensure you've pinned a location on the map.";
                    // Don't return - we'll still try to save with zeros
                }
                
                // Ensure any empty location fields are set to a default value
                report.Purok = string.IsNullOrEmpty(report.Purok) ? "Unknown" : report.Purok;
                report.Barangay = string.IsNullOrEmpty(report.Barangay) ? "Unknown" : report.Barangay;
                report.City = string.IsNullOrEmpty(report.City) ? "Unknown" : report.City;
                report.Country = string.IsNullOrEmpty(report.Country) ? "Philippines" : report.Country;
                
                // Set AdditionalInfo to null if empty
                report.AdditionalInfo = string.IsNullOrWhiteSpace(report.AdditionalInfo) ? null : report.AdditionalInfo;
                
                // Debug: Add the report object explicitly
                _context.DisasterReports.Add(report);
                
                // Debug: Save changes and capture how many records were affected
                int recordsAffected = await _context.SaveChangesAsync();
                
                if (recordsAffected > 0)
                {
                    TempData["SuccessMessage"] = "Your incident report has been successfully submitted and is pending review.";
                    return RedirectToAction(nameof(Dashboard));
                }
                else
                {
                    TempData["ErrorMessage"] = "No records were affected when saving to database.";
                    return View(report);
                }
            }
            catch (Exception ex)
            {
                // Capture the exception details for debugging
                TempData["ErrorMessage"] = "Error submitting report: " + ex.Message;
                if (ex.InnerException != null)
                {
                    TempData["InnerErrorMessage"] = "Inner error: " + ex.InnerException.Message;
                }
                return View(report);
            }
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
        
        // Post community update
        public IActionResult PostUpdate()
        {
            return View();
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostUpdate(CommunityUpdate update, IFormFile Image)
        {
            try
            {
                // Get the current user's ID
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    TempData["ErrorMessage"] = "User not authenticated.";
                    return View(update);
                }

                // Set required fields
                update.UserId = userId;
                update.DatePosted = DateTime.Now;
                update.ModerationStatus = ModerationStatus.Pending;

                // Handle image upload if provided
                if (Image != null)
                {
                    var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "community_posts");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueFileName = $"{Guid.NewGuid()}_{Image.FileName}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await Image.CopyToAsync(fileStream);
                    }

                    update.ImageUrl = $"/uploads/community_posts/{uniqueFileName}";
                }
                else
                {
                    update.ImageUrl = "/images/no-image.png"; // Set a default image URL
                }

                // Ensure location fields are properly set
                if (string.IsNullOrEmpty(update.Location))
                {
                    update.Location = "Not specified";
                }

                // If coordinates are not provided, set them to null
                if (update.Latitude == 0 && update.Longitude == 0)
                {
                    update.Latitude = null;
                    update.Longitude = null;
                }

                // Debug: Log the update object before saving
                TempData["DebugInfo"] = $"Attempting to save post: Content={update.Content}, Type={update.Type}, UserId={update.UserId}";

                // Add the post to the database
                _context.CommunityUpdates.Add(update);
                
                // Debug: Log the entity state
                var entry = _context.Entry(update);
                TempData["DebugInfo"] += $", State={entry.State}";

                var result = await _context.SaveChangesAsync();

                if (result > 0)
                {
                    TempData["SuccessMessage"] = "Your post has been submitted and is pending approval.";
                return RedirectToAction(nameof(CommunityFeed));
            }
                else
                {
                    TempData["ErrorMessage"] = "Failed to save the post. No records were affected.";
                    return View(update);
                }
            }
            catch (DbUpdateException dbEx)
            {
                TempData["ErrorMessage"] = "Database error: " + dbEx.Message;
                if (dbEx.InnerException != null)
                {
                    TempData["InnerErrorMessage"] = "Inner error: " + dbEx.InnerException.Message;
                }
                return View(update);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error creating post: " + ex.Message;
                if (ex.InnerException != null)
                {
                    TempData["InnerErrorMessage"] = "Inner error: " + ex.InnerException.Message;
            }
            return View(update);
            }
        }
        
        // Access educational resources
        public async Task<IActionResult> EducationalResources()
        {
            var resources = await _context.EducationalResources
                .Where(r => r.ApprovalStatus == ApprovalStatus.Approved)
                .OrderBy(r => r.Title)
                .ToListAsync();
                
            return View(resources);
        }
        
        // View educational resource details
        public async Task<IActionResult> ResourceDetails(int id)
        {
            var resource = await _context.EducationalResources
                .FirstOrDefaultAsync(r => r.Id == id && r.ApprovalStatus == ApprovalStatus.Approved);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            return View(resource);
        }
        
        // Download educational resource as PDF
        public async Task<IActionResult> DownloadResourcePdf(int id)
        {
            var resource = await _context.EducationalResources
                .Include(r => r.CreatedBy)
                .FirstOrDefaultAsync(r => r.Id == id && r.ApprovalStatus == ApprovalStatus.Approved);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            try
            {
                byte[] pdfBytes = _pdfService.GenerateResourcePdf(resource);
                
                // Generate a clean filename
                string safeFileName = resource.Title.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
                string fileName = $"{safeFileName}_{DateTime.Now:yyyyMMdd}.pdf";
                
                // Return the PDF as a file download
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                // Log error and redirect back to resource details with error
                TempData["ErrorMessage"] = "Error generating PDF: " + ex.Message;
                return RedirectToAction(nameof(ResourceDetails), new { id });
            }
        }
        
        // View alerts
        public async Task<IActionResult> Alerts(string severity = null)
        {
            var query = _context.DisasterReports.AsQueryable()
                .Where(r => r.Status == ReportStatus.Verified);

            if (!string.IsNullOrEmpty(severity))
            {
                if (Enum.TryParse<SeverityLevel>(severity, out var severityLevel))
                {
                    query = query.Where(r => r.Severity == severityLevel);
                }
            }

            var alerts = await query.OrderByDescending(r => r.DateReported).ToListAsync();
            return View(alerts);
        }
        
        // View incident map
        public IActionResult IncidentMap()
        {
            return View();
        }
        
        // API endpoint to get map data
        [HttpGet]
        public async Task<JsonResult> GetMapData(int? status, DateTime? dateFrom, DateTime? dateTo)
        {
            var query = _context.DisasterReports
                .Include(r => r.AssignedTo)
                .AsQueryable();

            // Only show In Progress and Resolved by default if no status filter
            if (status.HasValue)
            {
                query = query.Where(r => (int)r.Status == status.Value);
            }
            else
            {
                query = query.Where(r => r.Status == ReportStatus.InProgress || r.Status == ReportStatus.Resolved);
            }

            if (dateFrom.HasValue)
            {
                query = query.Where(r => r.DateReported >= dateFrom.Value);
            }
            if (dateTo.HasValue)
            {
                query = query.Where(r => r.DateReported <= dateTo.Value);
            }

            var reports = await query
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
                    r.City,
                    AssignedTo = r.AssignedTo != null ? new {
                        Name = $"{r.AssignedTo.FirstName} {r.AssignedTo.LastName}",
                        Organization = r.AssignedTo.OrganizationName
                    } : null
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