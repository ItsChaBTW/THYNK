using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using THYNK.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace THYNK.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(ILogger<HomeController> logger, UserManager<ApplicationUser> userManager)
    {
        _logger = logger;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        // Check if user is authenticated
        if (User.Identity.IsAuthenticated)
        {
            // Get current user
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);

            // Check if user is in Community role
            bool isInCommunityRole = await _userManager.IsInRoleAsync(user, "Community");
            
            // Redirect to Community Dashboard if user is in Community role
            if (isInCommunityRole)
            {
                return RedirectToAction("Dashboard", "Community");
            }
        }
        
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
