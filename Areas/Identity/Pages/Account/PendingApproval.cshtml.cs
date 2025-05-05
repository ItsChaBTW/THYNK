using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using THYNK.Models;

namespace THYNK.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class PendingApprovalModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;

        public PendingApprovalModel(SignInManager<ApplicationUser> signInManager)
        {
            _signInManager = signInManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // Ensure the user is signed out when viewing this page
            if (User.Identity.IsAuthenticated)
            {
                await _signInManager.SignOutAsync();
            }
            
            return Page();
        }
    }
} 