using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using THYNK.Data;
using THYNK.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authentication;
using System.Text.Json;
using System.IO;
using Microsoft.AspNetCore.Http;

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
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Get recent disaster reports assigned to this LGU organization
            var recentReports = await _context.DisasterReports
                .Where(r => r.AssignedToId == userId || 
                           (r.Status == ReportStatus.InProgress && r.AssignedTo.OrganizationName == currentUser.OrganizationName))
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
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }

            IQueryable<DisasterReport> reportsQuery = _context.DisasterReports
                .Include(r => r.AssignedTo)
                .Where(r => r.AssignedToId == userId || 
                           (r.Status == ReportStatus.InProgress && r.AssignedTo.OrganizationName == currentUser.OrganizationName));
            
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
            string responderName = $"{currentUser?.FirstName ?? "Unknown"} {currentUser?.LastName ?? "User"}";
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
                report.ResolvedAt = DateTime.Now;
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
                alert.IssuedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

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
                .Include(c => c.User)
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

        // Profile management page
        public IActionResult Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = _userManager.FindByIdAsync(userId).Result as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Get or initialize notification preferences
            var notificationPreferences = _context.NotificationPreferences
                .FirstOrDefaultAsync(np => np.UserId == userId).Result;
                
            if (notificationPreferences == null)
            {
                notificationPreferences = new NotificationPreferences
                {
                    UserId = userId,
                    EmailEnabled = true,
                    SmsEnabled = false,
                    ReportUpdatesEnabled = true,
                    CommunityActivityEnabled = true
                };
                
                _context.NotificationPreferences.Add(notificationPreferences);
                _context.SaveChangesAsync().Wait();
            }
            
            // Pass notification preferences to the view
            currentUser.NotificationPreferences = notificationPreferences;
            
            // Get available jurisdiction areas
            ViewBag.AvailableAreas = _context.Users
                .Where(u => !string.IsNullOrEmpty(u.CityMunicipalityName))
                .Select(u => u.CityMunicipalityName)
                .Distinct()
                .ToListAsync().Result;
            
            return View(currentUser);
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(LGUUser model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    // Update user fields
                    currentUser.FirstName = model.FirstName;
                    currentUser.LastName = model.LastName;
                    currentUser.Position = model.Position;
                    currentUser.Department = model.Department;
                    currentUser.Email = model.Email;
                    currentUser.PhoneNumber = model.PhoneNumber;
                    currentUser.Bio = model.Bio;
                    
                    // Update jurisdiction area if provided
                    if (!string.IsNullOrEmpty(model.JurisdictionArea))
                    {
                        currentUser.CityMunicipalityName = model.JurisdictionArea;
                    }
                    
                    // Save changes
                    await _userManager.UpdateAsync(currentUser);
                    
                    TempData["SuccessMessage"] = "Profile information updated successfully.";
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error updating profile: {ex.Message}");
                    ModelState.AddModelError("", "An error occurred while updating your profile.");
                }
            }
            
            return RedirectToAction(nameof(Profile));
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePassword(string CurrentPassword, string NewPassword, string ConfirmPassword)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId);
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            if (string.IsNullOrEmpty(CurrentPassword) || string.IsNullOrEmpty(NewPassword) || string.IsNullOrEmpty(ConfirmPassword))
            {
                TempData["ErrorMessage"] = "All password fields are required.";
                return RedirectToAction(nameof(Profile));
            }
            
            if (NewPassword != ConfirmPassword)
            {
                TempData["ErrorMessage"] = "New password and confirmation password do not match.";
                return RedirectToAction(nameof(Profile));
            }
            
            var passwordCheck = await _userManager.CheckPasswordAsync(currentUser, CurrentPassword);
            if (!passwordCheck)
            {
                TempData["ErrorMessage"] = "Current password is incorrect.";
                return RedirectToAction(nameof(Profile));
            }
            
            // Change password
            var result = await _userManager.ChangePasswordAsync(currentUser, CurrentPassword, NewPassword);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Password updated successfully.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to update password: " + string.Join(", ", result.Errors.Select(e => e.Description));
            }
            
            return RedirectToAction(nameof(Profile));
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateNotifications(bool EmailNotifications, bool SmsNotifications, bool ReportUpdates, bool CommunityActivity)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Get existing notification preferences
            var preferences = await _context.NotificationPreferences
                .FirstOrDefaultAsync(np => np.UserId == userId);
                
            if (preferences == null)
            {
                // Create new notification preferences if they don't exist
                preferences = new NotificationPreferences
                {
                    UserId = userId,
                    EmailEnabled = EmailNotifications,
                    SmsEnabled = SmsNotifications,
                    ReportUpdatesEnabled = ReportUpdates,
                    CommunityActivityEnabled = CommunityActivity
                };
                
                _context.NotificationPreferences.Add(preferences);
            }
            else
            {
                // Update existing preferences
                preferences.EmailEnabled = EmailNotifications;
                preferences.SmsEnabled = SmsNotifications;
                preferences.ReportUpdatesEnabled = ReportUpdates;
                preferences.CommunityActivityEnabled = CommunityActivity;
                
                _context.NotificationPreferences.Update(preferences);
            }
            
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Notification preferences updated successfully.";
            
            return RedirectToAction(nameof(Profile));
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfilePhoto(IFormFile ProfilePhoto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId);
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            if (ProfilePhoto != null && ProfilePhoto.Length > 0)
            {
                // Check file size (max 2MB)
                if (ProfilePhoto.Length > 2 * 1024 * 1024)
                {
                    TempData["ErrorMessage"] = "Profile photo must be less than 2MB.";
                    return RedirectToAction(nameof(Profile));
                }
                
                // Check file type
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif" };
                if (!allowedTypes.Contains(ProfilePhoto.ContentType.ToLower()))
                {
                    TempData["ErrorMessage"] = "Only JPEG, PNG, and GIF images are allowed.";
                    return RedirectToAction(nameof(Profile));
                }
                
                try
                {
                    // Create directory if it doesn't exist
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profile-photos");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    
                    // Generate unique filename
                    var uniqueFileName = $"{userId}_{DateTime.Now.Ticks}{Path.GetExtension(ProfilePhoto.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    
                    // Save the file
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await ProfilePhoto.CopyToAsync(fileStream);
                    }
                    
                    // Update user's profile photo URL
                    currentUser.ProfilePhotoUrl = $"/uploads/profile-photos/{uniqueFileName}";
                    await _userManager.UpdateAsync(currentUser);
                    
                    TempData["SuccessMessage"] = "Profile photo updated successfully.";
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error updating profile photo: {ex.Message}");
                    TempData["ErrorMessage"] = "An error occurred while updating your profile photo.";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Please select a profile photo to upload.";
            }
            
            return RedirectToAction(nameof(Profile));
        }
        
        [HttpGet]
        public async Task<IActionResult> DeleteAccount()
        {
            return View();
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDeleteAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId);
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Sign the user out
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            
            // Attempt to delete the user
            var result = await _userManager.DeleteAsync(currentUser);
            
            if (result.Succeeded)
            {
                return RedirectToAction("Index", "Home");
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to delete account: " + string.Join(", ", result.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Profile));
            }
        }

        // API endpoint to get map data
        [HttpGet]
        public async Task<JsonResult> GetMapData(int? status, DateTime? dateFrom, DateTime? dateTo)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new List<object>());
            }

            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            if (currentUser == null)
            {
                return Json(new List<object>());
            }

            // Start with base query for incidents assigned to this LGU
            var query = _context.DisasterReports
                .Include(r => r.AssignedTo)
                .Where(r => r.AssignedToId == userId || 
                       (r.Status == ReportStatus.InProgress && r.AssignedTo != null && r.AssignedTo.OrganizationName == currentUser.OrganizationName));

            // Apply status filter if provided
            if (status.HasValue)
            {
                query = query.Where(r => (int)r.Status == status.Value);
            }
            else
            {
                // By default, only show In Progress and Resolved
                query = query.Where(r => r.Status == ReportStatus.InProgress || r.Status == ReportStatus.Resolved);
            }

            // Apply date filters if provided
            if (dateFrom.HasValue)
            {
                query = query.Where(r => r.DateReported >= dateFrom.Value);
            }
            if (dateTo.HasValue)
            {
                query = query.Where(r => r.DateReported <= dateTo.Value);
            }

            // Get the filtered incidents
            var incidents = await query
                .OrderByDescending(r => r.DateReported)
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Description,
                    r.Location,
                    r.Barangay,
                    r.City,
                    r.Latitude,
                    r.Longitude,
                    r.Type,
                    r.Status,
                    r.Severity,
                    r.DateReported,
                    AssignedTo = r.AssignedTo != null ? new
                    {
                        r.AssignedTo.Id,
                        Name = r.AssignedTo.FirstName + " " + r.AssignedTo.LastName,
                        Organization = r.AssignedTo.OrganizationName
                    } : null
                })
                .ToListAsync();

            return Json(incidents);
        }

        // Post a community update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostUpdate(CommunityUpdate update, IFormFile Image)
        {
            try
            {
                // Add these debug lines
                _logger.LogInformation("PostUpdate action called");
                _logger.LogInformation($"Content: {update.Content}");
                _logger.LogInformation($"Type: {update.Type}");
                _logger.LogInformation($"UserId: {User.FindFirstValue(ClaimTypes.NameIdentifier)}");

                // Get the current user's ID
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User not authenticated");
                    TempData["ErrorMessage"] = "User not authenticated.";
                    return View(update);
                }

                _logger.LogInformation($"User ID: {userId}");

                var user = await _userManager.FindByIdAsync(userId) as LGUUser;
                if (user == null)
                {
                    _logger.LogWarning($"PostUpdate failed: User not found for ID {userId}");
                    return NotFound();
                }

                // Set the required fields
                update.UserId = userId;
                update.DatePosted = DateTime.Now;
                update.ModerationStatus = ModerationStatus.Approved; // Automatically approve LGU/SLU posts
                update.ImageUrl = "/images/no-image.png"; // Set default image URL

                // Set optional fields if not provided
                if (string.IsNullOrEmpty(update.Location))
                {
                    update.Location = "Not specified";
                }

                _logger.LogInformation($"Created CommunityUpdate object: {JsonSerializer.Serialize(update)}");

                // Handle image upload if provided
                if (Image != null && Image.Length > 0)
                {
                    _logger.LogInformation($"Processing image upload: {Image.FileName}, Size: {Image.Length}");
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "community");
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

                    update.ImageUrl = $"/uploads/community/{uniqueFileName}";
                    _logger.LogInformation($"Image saved to: {update.ImageUrl}");
                }

                _context.CommunityUpdates.Add(update);
                _logger.LogInformation("Added update to context");

                var saveResult = await _context.SaveChangesAsync();
                _logger.LogInformation($"SaveChangesAsync result: {saveResult} rows affected");

                TempData["SuccessMessage"] = "Your post has been published successfully.";
                return RedirectToAction(nameof(CommunityFeed));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PostUpdate action");
                TempData["ErrorMessage"] = "An error occurred while posting your update. Please try again.";
                return View(update);
            }
        }

        // Analytics dashboard
        public async Task<IActionResult> Analytics()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Get total reports count
            var totalReports = await _context.DisasterReports
                .Where(r => r.AssignedToId == userId || 
                           (r.AssignedTo.OrganizationName == currentUser.OrganizationName))
                .CountAsync();
                
            // Get resolved reports count
            var resolvedReports = await _context.DisasterReports
                .Where(r => (r.AssignedToId == userId || 
                           (r.AssignedTo.OrganizationName == currentUser.OrganizationName)) &&
                           r.Status == ReportStatus.Resolved)
                .CountAsync();
                
            // Calculate average response time (in hours)
            var reportsWithResolution = await _context.DisasterReports
                .Where(r => (r.AssignedToId == userId || 
                           (r.AssignedTo.OrganizationName == currentUser.OrganizationName)) &&
                           r.Status == ReportStatus.Resolved &&
                           r.ResolvedAt.HasValue &&
                           r.AssignedAt.HasValue)
                .ToListAsync();
                
            double avgResponseTime = 0;
            if (reportsWithResolution.Count > 0)
            {
                avgResponseTime = reportsWithResolution
                    .Average(r => (r.ResolvedAt.Value - r.AssignedAt.Value).TotalHours);
            }
            
            // Calculate community engagement (percentage of reports with comments)
            var communityEngagement = 0;
            // Implementation would depend on how comments are tracked in your system
            
            // Get geographic data for the map (in Negros Occidental area)
            var geoData = await _context.DisasterReports
                .Where(r => r.AssignedToId == userId || 
                           (r.AssignedTo.OrganizationName == currentUser.OrganizationName))
                .Where(r => r.Latitude != 0 && r.Longitude != 0)
                // Focus on Negros Occidental by filtering based on coordinates
                .Where(r => r.Longitude >= 122.27 && r.Longitude <= 123.55 && 
                           r.Latitude >= 9.85 && r.Latitude <= 11.05)
                .Select(r => new {
                    r.Id,
                    r.Title,
                    r.Type,
                    r.Status,
                    r.Severity,
                    r.Latitude,
                    r.Longitude,
                    r.Location,
                    r.Barangay,
                    r.City,
                    ReportDate = r.DateReported,
                    IsResolved = r.Status == ReportStatus.Resolved
                })
                .ToListAsync();
            
            _logger.LogInformation($"Retrieved {geoData.Count} geographic data points for Negros Occidental");
            
            // Calculate district-based counts (for Negros Occidental)
            // Simplified approach - in real implementation you'd have proper district mapping
            var northDistrictCount = geoData.Count(r => 
                r.City?.ToLower().Contains("sagay") == true || 
                r.City?.ToLower().Contains("cadiz") == true || 
                r.City?.ToLower().Contains("escalante") == true ||
                r.City?.ToLower().Contains("san carlos") == true);
                
            var centralDistrictCount = geoData.Count(r => 
                r.City?.ToLower().Contains("bacolod") == true ||
                r.City?.ToLower().Contains("talisay") == true ||
                r.City?.ToLower().Contains("silay") == true);
                
            var southDistrictCount = geoData.Count(r => 
                r.City?.ToLower().Contains("himamaylan") == true ||
                r.City?.ToLower().Contains("kabankalan") == true ||
                r.City?.ToLower().Contains("sipalay") == true);
            
            ViewBag.TotalReports = totalReports;
            ViewBag.ResolvedReports = resolvedReports;
            ViewBag.AverageResponseTime = $"{avgResponseTime:F1}h";
            ViewBag.CommunityEngagement = $"{communityEngagement}%";
            ViewBag.GeoData = geoData;
            ViewBag.NorthDistrictCount = northDistrictCount;
            ViewBag.CentralDistrictCount = centralDistrictCount;
            ViewBag.SouthDistrictCount = southDistrictCount;
            
            return View();
        }
        
        // Chat Support for LGU
        public async Task<IActionResult> ChatSupport(int? chatId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Extract LGU jurisdiction details from the current user
            string lgaName = currentUser.OrganizationName;
            
            // Get chats that match this LGU's jurisdiction
            var activeChats = await _context.ChatSessions
                .Include(c => c.User)
                .Include(c => c.Messages)
                .Where(c => c.Status != ChatStatus.Closed)
                .Where(c => 
                    // Already assigned to this specific LGU user
                    c.AssignedToId == userId ||
                    // OR not assigned yet AND matches LGU jurisdiction
                    (c.AssignedToId == null && 
                     (
                         // Match based on user's location (city/municipality)
                         (c.User != null && 
                          ((c.User.CityMunicipalityName != null && c.User.CityMunicipalityName.Contains(currentUser.CityMunicipalityName ?? "")) ||
                           (c.User.BarangayName != null && c.User.BarangayName.Contains(currentUser.BarangayName ?? "")))
                         ) ||
                         // OR match based on chat category (can contain LGU name or area)
                         (c.Category != null && c.Category.Contains(lgaName)) ||
                         // OR match based on chat title (can mention area name)
                         (c.Title != null && (
                             c.Title.Contains(currentUser.CityMunicipalityName ?? "") || 
                             c.Title.Contains(currentUser.BarangayName ?? "") ||
                             c.Title.Contains(lgaName)
                         ))
                     )
                    )
                )
                .OrderByDescending(c => c.StartTime)
                .ToListAsync();
                
            ViewBag.ActiveChats = activeChats;
            
            // If a specific chat is selected, get its details
            if (chatId.HasValue)
            {
                var selectedChat = await _context.ChatSessions
                    .Include(c => c.User)
                    .Include(c => c.Messages.OrderBy(m => m.Timestamp))
                    .FirstOrDefaultAsync(c => c.Id == chatId.Value);
                    
                if (selectedChat != null)
                {
                    // Verify this chat belongs to this LGU's jurisdiction before allowing access
                    bool isAssignedToThisUser = selectedChat.AssignedToId == userId;
                    bool matchesJurisdiction = false;
                    
                    if (selectedChat.User != null)
                    {
                        matchesJurisdiction = 
                            (selectedChat.User.CityMunicipalityName != null && selectedChat.User.CityMunicipalityName.Contains(currentUser.CityMunicipalityName ?? "")) ||
                            (selectedChat.User.BarangayName != null && selectedChat.User.BarangayName.Contains(currentUser.BarangayName ?? ""));
                    }
                    
                    bool matchesCategory = selectedChat.Category != null && selectedChat.Category.Contains(lgaName);
                    bool matchesTitle = selectedChat.Title != null && (
                        selectedChat.Title.Contains(currentUser.CityMunicipalityName ?? "") || 
                        selectedChat.Title.Contains(currentUser.BarangayName ?? "") ||
                        selectedChat.Title.Contains(lgaName)
                    );
                    
                    if (isAssignedToThisUser || matchesJurisdiction || matchesCategory || matchesTitle)
                    {
                        // Auto-assign this chat to the current LGU user if not already assigned
                        if (string.IsNullOrEmpty(selectedChat.AssignedToId))
                        {
                            selectedChat.AssignedToId = userId;
                            await _context.SaveChangesAsync();
                            
                            // Add system message indicating assignment
                            var assignmentMessage = new ChatMessage
                            {
                                ChatSessionId = selectedChat.Id,
                                SenderId = null,  // System message
                                Content = $"Chat assigned to {currentUser.FirstName} {currentUser.LastName} ({currentUser.OrganizationName}).",
                                Timestamp = DateTime.Now
                            };
                            
                            _context.ChatMessages.Add(assignmentMessage);
                            await _context.SaveChangesAsync();
                        }
                        
                        ViewBag.SelectedChat = selectedChat;
                        ViewBag.SelectedChatId = selectedChat.Id;
                    }
                    else
                    {
                        // This chat doesn't belong to this LGU's jurisdiction
                        TempData["ErrorMessage"] = "You don't have access to this chat as it's not in your jurisdiction.";
                    }
                }
            }
            
            return View();
        }
        
        // Send message in chat for LGU
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int sessionId, string message)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(c => c.Id == sessionId);
                
            if (session == null)
            {
                return NotFound();
            }
            
            if (session.Status == ChatStatus.Closed)
            {
                TempData["ErrorMessage"] = "This chat session has been closed.";
                return RedirectToAction(nameof(ChatSupport), new { chatId = sessionId });
            }
            
            // Verify this LGU user has jurisdiction over this chat
            bool hasJurisdiction = session.AssignedToId == userId;
            
            if (!hasJurisdiction)
            {
                // Check if chat matches jurisdiction based on location
                var chatUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == session.UserId);
                if (chatUser != null)
                {
                    hasJurisdiction = 
                        (chatUser.CityMunicipalityName != null && 
                         chatUser.CityMunicipalityName.Contains(currentUser.CityMunicipalityName ?? "")) ||
                        (chatUser.BarangayName != null && 
                         chatUser.BarangayName.Contains(currentUser.BarangayName ?? ""));
                }
                
                // Also check title and category
                hasJurisdiction = hasJurisdiction || 
                    (session.Category != null && session.Category.Contains(currentUser.OrganizationName)) ||
                    (session.Title != null && (
                        session.Title.Contains(currentUser.CityMunicipalityName ?? "") || 
                        session.Title.Contains(currentUser.BarangayName ?? "") ||
                        session.Title.Contains(currentUser.OrganizationName)
                    ));
            }
            
            if (!hasJurisdiction)
            {
                TempData["ErrorMessage"] = "You don't have access to this chat as it's not in your jurisdiction.";
                return RedirectToAction(nameof(ChatSupport));
            }
            
            // Add the message
            var chatMessage = new ChatMessage
            {
                ChatSessionId = sessionId,
                SenderId = userId,
                Content = message,
                Timestamp = DateTime.Now
            };
            
            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();
            
            // Always ensure this chat is assigned to the current LGU
            if (string.IsNullOrEmpty(session.AssignedToId))
            {
                session.AssignedToId = userId;
                await _context.SaveChangesAsync();
                
                // Add system message indicating assignment
                var assignmentMessage = new ChatMessage
                {
                    ChatSessionId = sessionId,
                    SenderId = null,  // System message
                    Content = $"Chat assigned to {currentUser.FirstName} {currentUser.LastName} ({currentUser.OrganizationName}).",
                    Timestamp = DateTime.Now
                };
                
                _context.ChatMessages.Add(assignmentMessage);
                await _context.SaveChangesAsync();
            }
            
            return RedirectToAction(nameof(ChatSupport), new { chatId = sessionId });
        }
        
        // Close chat for LGU
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseChat(int sessionId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentUser = await _userManager.FindByIdAsync(userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(c => c.Id == sessionId);
                
            if (session == null)
            {
                return NotFound();
            }
            
            // Verify this LGU user has jurisdiction over this chat
            bool hasJurisdiction = session.AssignedToId == userId;
            
            if (!hasJurisdiction)
            {
                TempData["ErrorMessage"] = "You don't have access to this chat as it's not in your jurisdiction.";
                return RedirectToAction(nameof(ChatSupport));
            }
            
            // Close the session
            session.Status = ChatStatus.Closed;
            session.EndTime = DateTime.Now;
            
            // Add system message indicating session closure
            var closureMessage = new ChatMessage
            {
                ChatSessionId = sessionId,
                SenderId = null,
                Content = $"Chat session closed by {currentUser.FirstName} {currentUser.LastName} from {currentUser.OrganizationName}.",
                Timestamp = DateTime.Now
            };
            
            _context.ChatMessages.Add(closureMessage);
            await _context.SaveChangesAsync();
            
            return RedirectToAction(nameof(ChatSupport));
        }
    }
} 