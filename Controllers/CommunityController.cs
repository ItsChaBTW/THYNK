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

            var alerts = await _context.Alerts
                .Include(a => a.User)
                .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now))
                .OrderByDescending(a => a.DateIssued)
                .ToListAsync();

            var viewModel = new CommunityFeedViewModel
            {
                Items = new List<FeedItem>()
            };

            // Add community updates
            foreach (var update in updates)
            {
                viewModel.Items.Add(new FeedItem
                {
                    Id = update.Id,
                    Type = "CommunityUpdate",
                    Title = update.Type.ToString(),
                    Message = update.Content,
                    DatePosted = update.DatePosted.ToUniversalTime(),
                    User = update.User,
                    ImageUrl = update.ImageUrl,
                    Location = update.Location,
                    Latitude = update.Latitude,
                    Longitude = update.Longitude
                });
            }

            // Add LGU alerts
            foreach (var alert in alerts)
            {
                viewModel.Items.Add(new FeedItem
                {
                    Id = alert.Id,
                    Type = "Alert",
                    Title = alert.Title,
                    Message = alert.Message,
                    DatePosted = alert.DateIssued.ToUniversalTime(),
                    User = alert.User,
                    ImageUrl = alert.ImagePath,
                    Location = alert.AffectedArea,
                    Severity = alert.Severity,
                    BackgroundStyle = alert.Severity == AlertSeverity.Info ? "bg-info" :
                                    alert.Severity == AlertSeverity.Warning ? "bg-warning" :
                                    alert.Severity == AlertSeverity.Danger ? "bg-danger" :
                                    alert.Severity == AlertSeverity.Critical ? "bg-dark" : "bg-info",
                    IconStyle = "fas fa-bell",
                    IssuedBy = alert.User is LGUUser lguUser ? lguUser.OrganizationName : "LGU"
                });
            }

            // Sort all items by date
            viewModel.Items = viewModel.Items.OrderByDescending(i => i.DatePosted).ToList();
                
            return View(viewModel);
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
                    return RedirectToAction(nameof(CommunityFeed));
                }

                // Validate required fields
                if (string.IsNullOrEmpty(update.Location))
                {
                    ModelState.AddModelError("Location", "Location is required");
                    TempData["ErrorMessage"] = "Please specify a location for your post.";
                    return RedirectToAction(nameof(CommunityFeed));
                }

                if (Image == null || Image.Length == 0)
                {
                    ModelState.AddModelError("Image", "Please upload an image");
                    TempData["ErrorMessage"] = "Please upload an image with your post.";
                    return RedirectToAction(nameof(CommunityFeed));
                }

                // Validate image size (max 5MB)
                if (Image.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("Image", "Image size must be less than 5MB");
                    TempData["ErrorMessage"] = "Image size must be less than 5MB.";
                    return RedirectToAction(nameof(CommunityFeed));
                }

                // Validate image type
                var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif" };
                if (!allowedTypes.Contains(Image.ContentType.ToLower()))
                {
                    ModelState.AddModelError("Image", "Only JPEG, PNG, and GIF images are allowed");
                    TempData["ErrorMessage"] = "Only JPEG, PNG, and GIF images are allowed.";
                    return RedirectToAction(nameof(CommunityFeed));
                }

                // Set required fields
                update.UserId = userId;
                update.DatePosted = DateTime.UtcNow;
                update.ModerationStatus = ModerationStatus.Pending;

                // Handle image upload
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

                // Add the post to the database
                _context.CommunityUpdates.Add(update);
                await _context.SaveChangesAsync();

                // Update dashboard stats after successful post creation
                await _hubContext.Clients.All.SendAsync("ReceiveDashboardStats",
                    await _context.LGUUsers.CountAsync(u => !u.IsApproved),
                    await _context.DisasterReports.CountAsync(r => r.Status == ReportStatus.Pending),
                    await _context.CommunityUpdates.CountAsync(p => p.ModerationStatus == ModerationStatus.Pending)
                );

                TempData["SuccessMessage"] = "Your post has been submitted and is pending approval.";
                return RedirectToAction(nameof(CommunityFeed));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "An error occurred while creating your post.";
                return RedirectToAction(nameof(CommunityFeed));
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
        public async Task<IActionResult> Alerts(string type = null, string notificationType = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            // Get active alerts
            var alertsQuery = _context.Alerts
                .Include(a => a.User)
                .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now));

            // Filter alerts by severity if specified
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<AlertSeverity>(type, out var severity))
            {
                alertsQuery = alertsQuery.Where(a => a.Severity == severity);
            }

            var alerts = await alertsQuery
                .OrderByDescending(a => a.DateIssued)
                .ToListAsync();

            // Get user notifications
            var notificationsQuery = _context.UserNotifications
                .Where(n => n.UserId == userId);

            // Filter notifications by type if specified
            if (!string.IsNullOrEmpty(notificationType))
            {
                notificationsQuery = notificationsQuery.Where(n => n.NotificationType == notificationType);
            }

            var notifications = await notificationsQuery
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            // Create view model
            var viewModel = new AlertsViewModel
            {
                Alerts = alerts,
                Notifications = notifications
            };

            return View(viewModel);
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

            if (status.HasValue)
            {
                query = query.Where(r => r.Status == (ReportStatus)status.Value);
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
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Description,
                    r.Latitude,
                    r.Longitude,
                    r.Status,
                    r.DateReported,
                    AssignedTo = r.AssignedTo != null ? new
                    {
                        r.AssignedTo.Id,
                        r.AssignedTo.FirstName,
                        r.AssignedTo.LastName,
                        OrganizationName = r.AssignedTo.GetType().Name == "LGUUser" ? 
                            ((LGUUser)r.AssignedTo).OrganizationName : "LGU"
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
                .Include(r => r.Ratings)
                    .ThenInclude(rating => rating.User)
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

            // Get user notifications
            var notificationsQuery = _context.UserNotifications
                .Where(n => n.UserId == userId);

            // Get active alerts
            var alertsQuery = _context.Alerts
                .Include(a => a.User)
                .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now));

            // Get notifications
            var notifications = await notificationsQuery
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .Select(n => new NotificationItem
                {
                    Id = n.Id,
                    Title = n.Title,
                    Message = n.Message,
                    NotificationType = n.NotificationType,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt,
                    RelatedEntityId = n.RelatedEntityId,
                    RelatedEntityType = n.RelatedEntityType
                })
                .ToListAsync();

            // Get read alert IDs for current user
            var readAlertIds = await _context.AlertReadStatus
                .Where(rs => rs.UserId == userId)
                .Select(rs => rs.AlertId)
                .ToListAsync();

            // Get alerts
            var alerts = await alertsQuery
                .OrderByDescending(a => a.DateIssued)
                .Take(5)
                .Select(a => new NotificationItem
                {
                    Id = a.Id,
                    Title = a.Title,
                    Message = a.Message,
                    NotificationType = a.Severity.ToString().ToLower(),
                    IsRead = readAlertIds.Contains(a.Id), // Mark as read if in readAlertIds
                    CreatedAt = a.DateIssued,
                    RelatedEntityId = a.Id,
                    RelatedEntityType = "Alert",
                    Severity = a.Severity,
                    AffectedArea = a.AffectedArea,
                    ImagePath = a.ImagePath,
                    IssuedBy = a.User.GetType().Name == "LGUUser" ? 
                        ((LGUUser)a.User).OrganizationName : "LGU"
                })
                .ToListAsync();

            // Combine notifications and alerts
            var allNotifications = notifications.Concat(alerts)
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .ToList();

            // Calculate total unread count (unread notifications + unread active alerts)
            var unreadNotificationsCount = await _context.UserNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);
            var unreadAlertsCount = await _context.Alerts
                .CountAsync(a => a.IsActive && 
                    (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now) &&
                    !_context.AlertReadStatus.Any(rs => rs.AlertId == a.Id && rs.UserId == userId));

            var totalUnreadCount = unreadNotificationsCount + unreadAlertsCount;

            return Json(new { notifications = allNotifications, unreadCount = totalUnreadCount });
        }

        // Mark a notification as read
        [HttpPost]
        public async Task<JsonResult> MarkNotificationAsRead([FromBody] MarkNotificationRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false });
            }

            var notification = await _context.UserNotifications
                .FirstOrDefaultAsync(n => n.Id == request.notificationId && n.UserId == userId);

            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            // Get updated unread count
            var unreadCount = await _context.UserNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            return Json(new { success = true, unreadCount });
        }

        public class MarkNotificationRequest
        {
            public int notificationId { get; set; }
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

        // Add function to create notifications for existing alerts
        [HttpPost]
        public async Task<IActionResult> CreateNotificationsForExistingAlerts()
        {
            try
            {
                // Get all active alerts that don't have corresponding notifications
                var activeAlerts = await _context.Alerts
                    .Include(a => a.User)
                    .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now))
                    .ToListAsync();

                // Get all users
                var users = await _context.Users.ToListAsync();
                int notificationsCreated = 0;

                foreach (var alert in activeAlerts)
                {
                    foreach (var user in users)
                    {
                        // Check if notification already exists for this alert and user
                        var existingNotification = await _context.UserNotifications
                            .AnyAsync(n => n.RelatedEntityId == alert.Id && 
                                         n.RelatedEntityType == "Alert" && 
                                         n.UserId == user.Id);

                        if (!existingNotification)
                        {
                            // Create new notification
                            var notification = new UserNotification
                            {
                                UserId = user.Id,
                                Title = alert.Title,
                                Message = alert.Message,
                                NotificationType = alert.Severity.ToString().ToLower(),
                                RelatedEntityId = alert.Id,
                                RelatedEntityType = "Alert",
                                CreatedAt = alert.DateIssued,
                                IsRead = false
                            };

                            _context.UserNotifications.Add(notification);
                            notificationsCreated++;
                        }
                    }
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Successfully created {notificationsCreated} notifications for existing alerts.";
                return RedirectToAction(nameof(CommunityFeed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notifications for existing alerts");
                TempData["ErrorMessage"] = "An error occurred while creating notifications for existing alerts.";
                return RedirectToAction(nameof(CommunityFeed));
            }
        }

        // Mark an alert as read
        [HttpPost]
        public async Task<JsonResult> MarkAlertAsRead([FromBody] MarkAlertRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false });
            }

            // Check if alert exists and is active
            var alert = await _context.Alerts
                .FirstOrDefaultAsync(a => a.Id == request.alertId && a.IsActive);

            if (alert == null)
            {
                return Json(new { success = false, message = "Alert not found" });
            }

            // Check if we already have a read status for this alert
            var readStatus = await _context.AlertReadStatus
                .FirstOrDefaultAsync(rs => rs.AlertId == request.alertId && rs.UserId == userId);

            if (readStatus == null)
            {
                // Create new read status
                readStatus = new AlertReadStatus
                {
                    AlertId = request.alertId,
                    UserId = userId,
                    ReadAt = DateTime.Now
                };
                _context.AlertReadStatus.Add(readStatus);
                await _context.SaveChangesAsync();
            }

            // Get updated unread count
            var unreadNotificationsCount = await _context.UserNotifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);
            var unreadAlertsCount = await _context.Alerts
                .CountAsync(a => a.IsActive && 
                    (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now) &&
                    !_context.AlertReadStatus.Any(rs => rs.AlertId == a.Id && rs.UserId == userId));

            var totalUnreadCount = unreadNotificationsCount + unreadAlertsCount;

            return Json(new { success = true, unreadCount = totalUnreadCount });
        }

        public class MarkAlertRequest
        {
            public int alertId { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RateReport(int id, int rating, string comment)
        {
            if (rating < 1 || rating > 5)
            {
                TempData["ErrorMessage"] = "Invalid rating value. Please provide a rating between 1 and 5.";
                return RedirectToAction(nameof(ReportDetails), new { id });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            var report = await _context.DisasterReports
                .Include(r => r.Ratings)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null)
            {
                TempData["ErrorMessage"] = "Report not found.";
                return RedirectToAction(nameof(MyReports));
            }

            if (report.Status != ReportStatus.Resolved)
            {
                TempData["ErrorMessage"] = "Only resolved reports can be rated.";
                return RedirectToAction(nameof(ReportDetails), new { id });
            }

            // Check if user has already rated this report
            var existingRating = await _context.DisasterReportRatings
                .FirstOrDefaultAsync(r => r.DisasterReportId == id && r.UserId == userId);

            if (existingRating != null)
            {
                TempData["ErrorMessage"] = "You have already rated this report. Multiple ratings are not allowed.";
                return RedirectToAction(nameof(ReportDetails), new { id });
            }

            var reportRating = new DisasterReportRating
            {
                DisasterReportId = id,
                UserId = userId,
                Rating = rating,
                Comment = comment,
                CreatedAt = DateTime.Now
            };

            _context.DisasterReportRatings.Add(reportRating);
            await _context.SaveChangesAsync();

            // Create a notification for the LGU user
            if (report.AssignedTo != null)
            {
                var notification = new UserNotification
                {
                    UserId = report.AssignedToId,
                    Title = "New Report Rating",
                    Message = $"A community member has rated your response to the report '{report.Title}'.",
                    NotificationType = "info",
                    RelatedEntityId = report.Id,
                    RelatedEntityType = "Report",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                };

                _context.UserNotifications.Add(notification);
                await _context.SaveChangesAsync();

                // Send real-time notification
                await _hubContext.Clients.Group($"user_{report.AssignedToId}")
                    .SendAsync("NotificationReceived", notification);
            }

            TempData["SuccessMessage"] = "Thank you for rating this report!";
            return RedirectToAction(nameof(ReportDetails), new { id });
        }
    }

    public class NotificationItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string NotificationType { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? RelatedEntityId { get; set; }
        public string RelatedEntityType { get; set; }
        public AlertSeverity? Severity { get; set; }
        public string AffectedArea { get; set; }
        public string ImagePath { get; set; }
        public string IssuedBy { get; set; }
    }
} 