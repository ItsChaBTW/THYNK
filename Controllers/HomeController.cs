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
        if (User.Identity != null && User.Identity.IsAuthenticated)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _userManager.FindByIdAsync(userId);

                if (user != null)
                {
                    // Check UserRole property (1 = LGU)
                    if (user.UserRole == UserRoleType.LGU)
                    {
                        return RedirectToAction("Dashboard", "LGU");
                    }
                    else if (user.UserRole == UserRoleType.Community)
                    {
                        return RedirectToAction("CommunityFeed", "Community");
                    }
                }
            }
        }
        return View();
    }

    // New action for user type selection
    public IActionResult UserTypeSelection()
    {
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
