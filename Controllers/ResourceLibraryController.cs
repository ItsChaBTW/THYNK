using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using THYNK.Data;
using THYNK.Models;
using Microsoft.AspNetCore.Authorization;
using System.IO;
using Microsoft.AspNetCore.StaticFiles;

namespace THYNK.Controllers
{
    [AllowAnonymous]
    public class ResourceLibraryController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ResourceLibraryController> _logger;
        
        public ResourceLibraryController(ApplicationDbContext context, ILogger<ResourceLibraryController> logger)
        {
            _context = context;
            _logger = logger;
        }
        
        // Public resource library listing
        public async Task<IActionResult> Index()
        {
            var resources = await _context.EducationalResources
                .Where(r => r.ApprovalStatus == ApprovalStatus.Approved)
                .OrderByDescending(r => r.DateAdded)
                .ToListAsync();
                
            return View(resources);
        }
        
        // Detailed resource view (public)
        public async Task<IActionResult> Details(int id)
        {
            var resource = await _context.EducationalResources
                .FirstOrDefaultAsync(r => r.Id == id && r.ApprovalStatus == ApprovalStatus.Approved);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            // Track view count
            resource.ViewCount = (resource.ViewCount ?? 0) + 1;
            await _context.SaveChangesAsync();
            
            return View(resource);
        }
        
        // Download resource (requires authentication)
        [Authorize]
        public async Task<IActionResult> Download(int id)
        {
            var resource = await _context.EducationalResources
                .FirstOrDefaultAsync(r => r.Id == id && r.ApprovalStatus == ApprovalStatus.Approved);
                
            if (resource == null)
            {
                return NotFound();
            }
            
            // Check if file exists
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", resource.FileUrl.TrimStart('/'));
            
            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogError($"File not found: {filePath}");
                return NotFound("The requested file could not be found.");
            }
            
            // Get content type
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(filePath, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            
            // Return the file
            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(fileBytes, contentType, Path.GetFileName(filePath));
        }
    }
} 