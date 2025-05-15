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
            // Allow ResourceLibrary and ViewResource to be accessed without authentication
            string actionName = context.RouteData.Values["action"]?.ToString();
            if (actionName == "ResourceLibrary" || actionName == "ViewResource")
            {
                await next();
                return;
            }
            
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Get resources created by this user
            var resources = await _context.EducationalResources
                .Where(r => r.CreatedById == userId)
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();
                
            return View(resources);
        }

        // GET: Create Educational Resource
        public IActionResult CreateResource()
        {
            return View();
        }

        // POST: Create Educational Resource
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateResource(EducationalResource resource, IFormFile resourceFile)
        {
            try
            {
                // Validate the model (except for FileUrl which we'll handle manually)
                if (!ModelState.IsValid)
                {
                    // We'll manually remove FileUrl errors as we'll handle that separately
                    if (ModelState.ContainsKey("FileUrl"))
                    {
                        ModelState["FileUrl"].Errors.Clear();
                    }
                    
                    // If there are still errors after removing FileUrl errors
                    if (ModelState.Values.Any(v => v.Errors.Count > 0))
                    {
                        // Log validation errors
                        foreach (var modelState in ModelState.Values)
                        {
                            foreach (var error in modelState.Errors)
                            {
                                _logger.LogWarning($"Validation error: {error.ErrorMessage}");
                            }
                        }
                        
                        // Return the view with validation errors
                        return View(resource);
                    }
                }
                
                // Set creation date and creator
                resource.DateAdded = DateTime.Now;
                resource.CreatedById = User.FindFirstValue(ClaimTypes.NameIdentifier);
                
                // Process file if provided
                if (resourceFile != null && resourceFile.Length > 0)
                {
                    // Validate file size (max 10MB)
                    if (resourceFile.Length > 10 * 1024 * 1024)
                    {
                        ModelState.AddModelError("resourceFile", "File size must be less than 10MB.");
                        return View(resource);
                    }
                    
                    // Validate file type
                    var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".txt", ".jpg", ".jpeg", ".png" };
                    var fileExtension = Path.GetExtension(resourceFile.FileName).ToLowerInvariant();
                    
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        ModelState.AddModelError("resourceFile", $"File type {fileExtension} is not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");
                        return View(resource);
                    }
                    
                    try
                    {
                        // Create the resource directory if it doesn't exist
                        string resourceDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "education_resource");
                        if (!Directory.Exists(resourceDirectory))
                        {
                            Directory.CreateDirectory(resourceDirectory);
                        }

                        // Generate unique filename
                        string uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                        string filePath = Path.Combine(resourceDirectory, uniqueFileName);
                        
                        // Save the file
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await resourceFile.CopyToAsync(stream);
                        }
                        
                        // Set file URL and size
                        resource.FileUrl = $"/uploads/education_resource/{uniqueFileName}";
                        resource.FileSizeKB = (int)(resourceFile.Length / 1024);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"File upload error: {ex.Message}");
                        ModelState.AddModelError("resourceFile", $"Error uploading file: {ex.Message}");
                        return View(resource);
                    }
                }
                else
                {
                    // If no file was uploaded, set a placeholder URL
                    // This addresses the "FileUrl field is required" error
                    resource.FileUrl = string.Empty;
                    resource.FileSizeKB = 0;
                    
                    // Add a warning message
                    TempData["WarningMessage"] = "No file was attached to this resource. You can edit the resource later to add a file.";
                }
                
                // Default values for unused fields if null to satisfy required constraints
                if (string.IsNullOrEmpty(resource.ExternalUrl))
                {
                    resource.ExternalUrl = string.Empty;
                }
                
                if (string.IsNullOrEmpty(resource.Tags))
                {
                    resource.Tags = string.Empty;
                }
                
                // Set default approval status for new resources
                resource.ApprovalStatus = ApprovalStatus.Pending;
                
                // Save resource to database
                try
                {
                    _context.Add(resource);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "Educational resource created successfully! It is now pending approval.";
                    return RedirectToAction(nameof(ManageResources));
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Database error when saving resource: {ex.Message}");
                    ModelState.AddModelError("", $"Error saving to database: {ex.Message}");
                    return View(resource);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error in CreateResource: {ex.Message}");
                ModelState.AddModelError("", $"An unexpected error occurred: {ex.Message}");
                return View(resource);
            }
        }

        // GET: Edit Educational Resource
        public async Task<IActionResult> EditResource(int id)
        {
            var resource = await _context.EducationalResources.FindAsync(id);
            
            if (resource == null)
            {
                return NotFound();
            }
            
            return View(resource);
        }

        // POST: Edit Educational Resource
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditResource(int id, EducationalResource resource, IFormFile resourceFile)
        {
            if (id != resource.Id)
            {
                return NotFound();
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    // Get the existing resource to keep file info if not changing
                    var existingResource = await _context.EducationalResources.AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == id);
                        
                    if (existingResource == null)
                    {
                        return NotFound();
                    }
                    
                    // Keep existing file info if no new file uploaded
                    if (resourceFile == null || resourceFile.Length == 0)
                    {
                        resource.FileUrl = existingResource.FileUrl;
                        resource.FileSizeKB = existingResource.FileSizeKB;
                    }
                    else
                    {
                        // Process new file
                        string resourceDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "education_resource");
                        if (!Directory.Exists(resourceDirectory))
                        {
                            Directory.CreateDirectory(resourceDirectory);
                        }

                        // Delete old file if it exists
                        if (!string.IsNullOrEmpty(existingResource.FileUrl))
                        {
                            string oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                                existingResource.FileUrl.TrimStart('/'));
                            if (System.IO.File.Exists(oldFilePath))
                            {
                                System.IO.File.Delete(oldFilePath);
                            }
                        }

                        // Save new file
                        string fileExtension = Path.GetExtension(resourceFile.FileName);
                        string uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                        string filePath = Path.Combine(resourceDirectory, uniqueFileName);
                        
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await resourceFile.CopyToAsync(stream);
                        }
                        
                        resource.FileUrl = $"/uploads/education_resource/{uniqueFileName}";
                        resource.FileSizeKB = (int)(resourceFile.Length / 1024);
                    }
                    
                    // Keep the original date added
                    resource.DateAdded = existingResource.DateAdded;
                    
                    // Default values for unused fields if null
                    if (string.IsNullOrEmpty(resource.ExternalUrl))
                    {
                        resource.ExternalUrl = string.Empty;
                    }
                    
                    if (string.IsNullOrEmpty(resource.FileUrl))
                    {
                        resource.FileUrl = string.Empty;
                    }
                    
                    if (string.IsNullOrEmpty(resource.Tags))
                    {
                        resource.Tags = string.Empty;
                    }
                    
                    _context.Update(resource);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "Educational resource updated successfully!";
                    return RedirectToAction(nameof(ManageResources));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ResourceExists(resource.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error updating resource: {ex.Message}");
                    ModelState.AddModelError("", "An error occurred while updating the resource.");
                }
            }
            
            return View(resource);
        }

        // GET: Delete Educational Resource Confirmation
        public async Task<IActionResult> DeleteResource(int id)
        {
            var resource = await _context.EducationalResources.FindAsync(id);
            
            if (resource == null)
            {
                return NotFound();
            }
            
            return View(resource);
        }

        // POST: Delete Educational Resource
        [HttpPost, ActionName("DeleteResource")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteResourceConfirmed(int id)
        {
            var resource = await _context.EducationalResources.FindAsync(id);
            
            if (resource == null)
            {
                return NotFound();
            }
            
            // Delete the file if it exists
            if (!string.IsNullOrEmpty(resource.FileUrl))
            {
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", 
                    resource.FileUrl.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            
            _context.EducationalResources.Remove(resource);
            await _context.SaveChangesAsync();
            
            TempData["SuccessMessage"] = "Educational resource deleted successfully!";
            return RedirectToAction(nameof(ManageResources));
        }

        // View resource details (visible to all users)
        [AllowAnonymous]
        public async Task<IActionResult> ViewResource(int id)
        {
            var resource = await _context.EducationalResources
                .FirstOrDefaultAsync(r => r.Id == id && r.ApprovalStatus == ApprovalStatus.Approved);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            // Track view count (optional)
            resource.ViewCount = (resource.ViewCount ?? 0) + 1;
            await _context.SaveChangesAsync();
            
            return View(resource);
        }

        // Resource Library (visible to all users)
        [AllowAnonymous]
        public async Task<IActionResult> ResourceLibrary()
        {
            var resources = await _context.EducationalResources
                .Where(r => r.ApprovalStatus == ApprovalStatus.Approved)
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();
                
            return View(resources);
        }
        
        private bool ResourceExists(int id)
        {
            return _context.EducationalResources.Any(e => e.Id == id);
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
        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // Fetch the current user directly from the database instead of using UserManager
            // to ensure we get the most up-to-date data including ProfilePhotoUrl
            var currentUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId) as LGUUser;
            
            if (currentUser == null)
            {
                return NotFound();
            }
            
            // Get or initialize notification preferences
            var notificationPreferences = await _context.NotificationPreferences
                .FirstOrDefaultAsync(np => np.UserId == userId);
                
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
                await _context.SaveChangesAsync();
            }
            
            // Pass notification preferences to the view
            currentUser.NotificationPreferences = notificationPreferences;
            
            // Get available jurisdiction areas
            ViewBag.AvailableAreas = await _context.Users
                .Where(u => !string.IsNullOrEmpty(u.CityMunicipalityName))
                .Select(u => u.CityMunicipalityName)
                .Distinct()
                .ToListAsync();
                
            // Debug log to show the current profile photo URL
            _logger.LogInformation($"User {userId} has profile photo URL: {currentUser.ProfilePhotoUrl}");
            
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
            _logger.LogInformation("UpdateProfilePhoto method called");
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _logger.LogInformation($"User ID: {userId}");
            var currentUser = await _userManager.FindByIdAsync(userId);
            
            if (currentUser == null)
            {
                _logger.LogWarning("User not found");
                return NotFound();
            }
            
            if (ProfilePhoto != null && ProfilePhoto.Length > 0)
            {
                _logger.LogInformation($"Received file: {ProfilePhoto.FileName}, ContentType: {ProfilePhoto.ContentType}, Size: {ProfilePhoto.Length} bytes");
                
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
                    // Get wwwroot path
                    var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    _logger.LogInformation($"Web root path: {webRootPath}");
                    
                    // Create directory if it doesn't exist
                    var uploadsFolder = Path.Combine(webRootPath, "uploads", "profile_pics");
                    _logger.LogInformation($"Uploads folder path: {uploadsFolder}");
                    
                    if (!Directory.Exists(uploadsFolder))
                    {
                        _logger.LogInformation("Creating uploads directory");
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    
                    // Generate unique filename
                    var uniqueFileName = $"{userId}_{DateTime.Now.Ticks}{Path.GetExtension(ProfilePhoto.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    _logger.LogInformation($"Saving file to: {filePath}");
                    
                    // Save the file
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await ProfilePhoto.CopyToAsync(fileStream);
                        _logger.LogInformation("File saved successfully");
                    }
                    
                    // Update user's profile photo URL with the correct path
                    var photoUrl = $"/uploads/profile_pics/{uniqueFileName}";
                    _logger.LogInformation($"Setting ProfilePhotoUrl to: {photoUrl}");
                    
                    // Force image to be created with valid permissions
                    System.IO.File.SetAttributes(filePath, FileAttributes.Normal);
                    
                    // Update in both currentUser and dbUser
                    currentUser.ProfilePhotoUrl = photoUrl;
                    
                    // Update with UserManager
                    var result = await _userManager.UpdateAsync(currentUser);
                    if (!result.Succeeded)
                    {
                        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                        _logger.LogError($"Error updating user profile via UserManager: {errors}");
                    }
                    else
                    {
                        _logger.LogInformation("UserManager update succeeded");
                    }
                    
                    // Also directly update the database as a fallback
                    var dbUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                    if (dbUser != null)
                    {
                        dbUser.ProfilePhotoUrl = photoUrl;
                        var saveChangesResult = await _context.SaveChangesAsync();
                        _logger.LogInformation($"Updated profile photo URL directly in database. Result: {saveChangesResult}");
                    }
                    else
                    {
                        _logger.LogWarning("Could not find user in database context");
                    }
                    
                    // Try getting the user again to verify the update
                    var verifyUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                    _logger.LogInformation($"Verification check - ProfilePhotoUrl after update: {verifyUser?.ProfilePhotoUrl}");
                    
                    TempData["SuccessMessage"] = "Profile photo updated successfully.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating profile photo: {ex.Message}");
                    TempData["ErrorMessage"] = $"An error occurred while updating your profile photo: {ex.Message}";
                }
            }
            else
            {
                _logger.LogWarning("No file received or file is empty");
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

        [HttpGet]
        public IActionResult TestFileSystem()
        {
            var result = new Dictionary<string, string>();
            
            try
            {
                // Test wwwroot path
                var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                result["WebRootPath"] = webRootPath;
                result["WebRootExists"] = Directory.Exists(webRootPath).ToString();
                
                // Test uploads directory
                var uploadsPath = Path.Combine(webRootPath, "uploads");
                result["UploadsPath"] = uploadsPath;
                result["UploadsExists"] = Directory.Exists(uploadsPath).ToString();
                
                // Test profile_pics directory
                var profilePicsPath = Path.Combine(uploadsPath, "profile_pics");
                result["ProfilePicsPath"] = profilePicsPath;
                result["ProfilePicsExists"] = Directory.Exists(profilePicsPath).ToString();
                
                // Try to create a test file
                var testFilePath = Path.Combine(profilePicsPath, "test.txt");
                System.IO.File.WriteAllText(testFilePath, "Test file created at " + DateTime.Now);
                result["TestFileCreated"] = "True";
                result["TestFilePath"] = testFilePath;
                
                // Try to read the test file
                var fileContent = System.IO.File.ReadAllText(testFilePath);
                result["TestFileContent"] = fileContent;
                
                // Get current user information
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var currentUser = _userManager.FindByIdAsync(userId).Result;
                
                if (currentUser != null)
                {
                    result["UserId"] = userId;
                    result["UserEmail"] = currentUser.Email;
                    result["CurrentProfilePhoto"] = currentUser.ProfilePhotoUrl ?? "null";
                }
                else
                {
                    result["UserInfo"] = "User not found";
                }
                
                // Try to get user from database context directly
                var dbUser = _context.Users.FirstOrDefault(u => u.Id == userId);
                if (dbUser != null)
                {
                    result["DbUserFound"] = "True";
                    result["DbUserProfilePhoto"] = dbUser.ProfilePhotoUrl ?? "null";
                }
                else
                {
                    result["DbUserFound"] = "False";
                }
            }
            catch (Exception ex)
            {
                result["Error"] = ex.Message;
                result["StackTrace"] = ex.StackTrace;
            }
            
            return Json(result);
        }
    }
} 