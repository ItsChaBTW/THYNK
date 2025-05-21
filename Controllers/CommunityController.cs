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
using Microsoft.AspNetCore.SignalR;
using THYNK.Hubs;
using Microsoft.AspNetCore.Identity;
using THYNK.Services;
using Microsoft.Extensions.Logging;

namespace THYNK.Controllers
{
    [Authorize(Roles = "Community")]
    public class CommunityController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IHubContext<AdminHub> _hubContext;
        private readonly IHubContext<CommunityHub> _communityHubContext;
        private readonly PdfService _pdfService;
        private readonly ILogger<CommunityController> _logger;

        public CommunityController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment webHostEnvironment,
            IHubContext<AdminHub> hubContext,
            IHubContext<CommunityHub> communityHubContext,
            PdfService pdfService,
            ILogger<CommunityController> logger)
        {
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
            _hubContext = hubContext;
            _communityHubContext = communityHubContext;
            _pdfService = pdfService;
            _logger = logger;
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
                    // Update dashboard stats after successful report submission
                    await _hubContext.Clients.All.SendAsync("ReceiveDashboardStats",
                        await _context.LGUUsers.CountAsync(u => !u.IsApproved),
                        await _context.DisasterReports.CountAsync(r => r.Status == ReportStatus.Pending),
                        await _context.CommunityUpdates.CountAsync(p => p.ModerationStatus == ModerationStatus.Pending)
                    );

                    // Send real-time update for Recent Reports
                    await _hubContext.Clients.All.SendAsync("RecentReportUpdated", ToRecentReportDto(report));

                    // Set report submission success data for the modal
                    TempData["ReportSubmitted"] = true;
                    TempData["ReportId"] = report.Id;
                    TempData["ReportTitle"] = report.Title;
                    TempData["ReportType"] = report.Type.ToString();
                    TempData["SuccessMessage"] = "Your incident report has been successfully submitted and is pending review.";
                    
                    return RedirectToAction(nameof(CommunityFeed));
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
        
        // Helper to format report for SignalR (same as AdminController)
        private object ToRecentReportDto(DisasterReport report)
        {
            return new {
                Id = report.Id,
                Title = report.Title,
                Type = report.Type.ToString(),
                Barangay = report.Barangay,
                City = report.City,
                DateReported = report.DateReported.ToString("MMM dd, yyyy"),
                Status = report.Status.ToString()
            };
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
        public async Task<IActionResult> PostUpdate(CommunityUpdate update, IFormFile Attachment)
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

                // Handle file attachment if provided
                if (Attachment != null)
                {
                    var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "community_posts");
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

                    update.ImageUrl = $"/uploads/community_posts/{uniqueFileName}";
                }
                else
                {
                    update.ImageUrl = null; // No attachment provided
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
                    // Update dashboard stats after successful post creation
                    await _hubContext.Clients.All.SendAsync("ReceiveDashboardStats",
                        await _context.LGUUsers.CountAsync(u => !u.IsApproved),
                        await _context.DisasterReports.CountAsync(r => r.Status == ReportStatus.Pending),
                        await _context.CommunityUpdates.CountAsync(p => p.ModerationStatus == ModerationStatus.Pending)
                    );

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
        public async Task<IActionResult> Alerts(string type = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            // Get user notifications
            var query = _context.UserNotifications
                .Where(n => n.UserId == userId);

            // Filter by notification type if specified
            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(n => n.NotificationType == type);
            }

            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            // Mark all as read
            foreach (var notification in notifications.Where(n => !n.IsRead))
            {
                notification.IsRead = true;
            }
            await _context.SaveChangesAsync();

            return View(notifications);
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
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            // Get user reports
            var reports = await _context.DisasterReports
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.DateReported)
                .ToListAsync();

            // Get unread notifications count
            var unreadCount = await _context.UserNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            ViewBag.UnreadNotificationsCount = unreadCount;
            _logger.LogInformation($"User {userId} viewing their reports. Unread notifications: {unreadCount}");

            return View(reports);
        }
        
        // View report details
        public async Task<IActionResult> ReportDetails(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            var report = await _context.DisasterReports
                .Include(r => r.User)
                .Include(r => r.AssignedTo)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);

            if (report == null)
            {
                return NotFound();
            }

            // Get unread notifications count
            var unreadCount = await _context.UserNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            ViewBag.UnreadNotificationsCount = unreadCount;
            _logger.LogInformation($"User {userId} viewing report {id}. Unread notifications: {unreadCount}");

            return View(report);
        }

        // Get notifications for the current user
        [HttpGet]
        public async Task<JsonResult> GetNotifications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { notifications = new List<object>(), unreadCount = 0 });
            }

            var notifications = await _context.UserNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .Select(n => new {
                    n.Id,
                    n.Title,
                    n.Message,
                    n.NotificationType,
                    n.IsRead,
                    n.CreatedAt,
                    n.RelatedEntityId,
                    n.RelatedEntityType
                })
                .ToListAsync();

            var unreadCount = await _context.UserNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            return Json(new { notifications, unreadCount });
        }

        // Mark a notification as read
        [HttpPost]
        public async Task<JsonResult> MarkNotificationRead(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false });
            }

            var notification = await _context.UserNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        // Mark all notifications as read
        [HttpPost]
        public async Task<JsonResult> MarkAllNotificationsRead()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false });
            }

            var notifications = await _context.UserNotifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // View evacuation sites
        [AllowAnonymous]
        public IActionResult EvacuationSite()
        {
            // Set the layout based on authentication status
            if (!User.Identity.IsAuthenticated)
            {
                // For anonymous users, use the default layout
                ViewBag.UseDefaultLayout = true;
            }
            // For authenticated users, the _CommunityLayout will be used by default
            
            return View();
        }

        [AllowAnonymous]
        public async Task<IActionResult> EvacuationSites(string type = null, bool? hasWater = null, bool? hasElectricity = null, 
            bool? hasMedicalSupplies = null, bool? isWheelchairAccessible = null)
        {
            IQueryable<EvacuationSite> sitesQuery = _context.EvacuationSites
                .Where(e => e.IsActive); // Only show active sites to community users
            
            // Filter by type if specified
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<EvacuationSiteType>(type, out var typeEnum))
            {
                sitesQuery = sitesQuery.Where(e => e.Type == typeEnum);
                ViewBag.CurrentTypeFilter = type;
            }
            
            // Apply facility filters
            if (hasWater.HasValue && hasWater.Value)
            {
                sitesQuery = sitesQuery.Where(e => e.HasWater);
                ViewBag.HasWaterFilter = true;
            }
            
            if (hasElectricity.HasValue && hasElectricity.Value)
            {
                sitesQuery = sitesQuery.Where(e => e.HasElectricity);
                ViewBag.HasElectricityFilter = true;
            }
            
            if (hasMedicalSupplies.HasValue && hasMedicalSupplies.Value)
            {
                sitesQuery = sitesQuery.Where(e => e.HasMedicalSupplies);
                ViewBag.HasMedicalSuppliesFilter = true;
            }
            
            if (isWheelchairAccessible.HasValue && isWheelchairAccessible.Value)
            {
                sitesQuery = sitesQuery.Where(e => e.IsWheelchairAccessible);
                ViewBag.IsWheelchairAccessibleFilter = true;
            }
            
            var sites = await sitesQuery.OrderBy(e => e.Name).ToListAsync();

            // Set different layout based on authentication status
            if (!User.Identity.IsAuthenticated)
            {
                // For anonymous users, use the default layout
                ViewBag.UseDefaultLayout = true;
            }

            return View(sites);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<JsonResult> GetNearbySites(double latitude, double longitude, int maxDistance = 10, string type = null, 
            bool? hasWater = null, bool? hasElectricity = null, bool? hasMedicalSupplies = null, bool? isWheelchairAccessible = null)
        {
            if (latitude == 0 && longitude == 0)
            {
                return Json(new { success = false, message = "Invalid coordinates" });
            }

            // Get all active evacuation sites
            var sitesQuery = _context.EvacuationSites
                .Where(s => s.IsActive);
            
            // Apply type filter if specified
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<EvacuationSiteType>(type, out var typeEnum))
            {
                sitesQuery = sitesQuery.Where(s => s.Type == typeEnum);
            }
            
            // Apply facility filters
            if (hasWater.HasValue && hasWater.Value)
            {
                sitesQuery = sitesQuery.Where(s => s.HasWater);
            }
            
            if (hasElectricity.HasValue && hasElectricity.Value)
            {
                sitesQuery = sitesQuery.Where(s => s.HasElectricity);
            }
            
            if (hasMedicalSupplies.HasValue && hasMedicalSupplies.Value)
            {
                sitesQuery = sitesQuery.Where(s => s.HasMedicalSupplies);
            }
            
            if (isWheelchairAccessible.HasValue && isWheelchairAccessible.Value)
            {
                sitesQuery = sitesQuery.Where(s => s.IsWheelchairAccessible);
            }
            
            var sites = await sitesQuery.ToListAsync();
            
            // Calculate distance to each site and filter by maxDistance (in kilometers)
            var nearbySites = sites
                .Select(site => {
                    // Calculate distance between coordinates using Haversine formula
                    double distance = CalculateDistance(latitude, longitude, site.Latitude, site.Longitude);
                    return new { Site = site, Distance = distance };
                })
                .Where(item => item.Distance <= maxDistance)
                .OrderBy(item => item.Distance)
                .Select(item => new {
                    id = item.Site.Id,
                    name = item.Site.Name,
                    address = item.Site.Address,
                    city = item.Site.City,
                    latitude = item.Site.Latitude,
                    longitude = item.Site.Longitude,
                    type = item.Site.Type.ToString(),
                    capacity = item.Site.Capacity,
                    description = item.Site.Description,
                    contactPerson = item.Site.ContactPerson,
                    contactNumber = item.Site.ContactNumber,
                    facilities = new {
                        water = item.Site.HasWater,
                        electricity = item.Site.HasElectricity,
                        medical = item.Site.HasMedicalSupplies,
                        internet = item.Site.HasInternet,
                        wheelchair = item.Site.IsWheelchairAccessible,
                        bathroom = item.Site.HasBathroomFacilities,
                        kitchen = item.Site.HasKitchen,
                        sleeping = item.Site.HasSleepingFacilities
                    },
                    distance = Math.Round(item.Distance, 1) // Round to 1 decimal place
                })
                .ToList();
            
            return Json(new { success = true, sites = nearbySites });
        }

        // Calculate distance between two GPS coordinates using Haversine formula
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double EarthRadius = 6371.0; // Earth's radius in kilometers
            
            // Convert degrees to radians
            double dLat = DegreesToRadians(lat2 - lat1);
            double dLon = DegreesToRadians(lon2 - lon1);
            
            // Haversine formula
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double distance = EarthRadius * c;
            
            return distance;
        }

        private double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
} 