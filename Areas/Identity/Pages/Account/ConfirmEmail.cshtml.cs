// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using THYNK.Models;
using System.ComponentModel.DataAnnotations;

namespace THYNK.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public ConfirmEmailModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string StatusMessage { get; set; }
        
        [BindProperty]
        public string Email { get; set; }
        
        [BindProperty]
        [Required(ErrorMessage = "Confirmation code is required")]
        [Display(Name = "Confirmation Code")]
        [StringLength(7, MinimumLength = 7, ErrorMessage = "The confirmation code must be 7 characters")]
        public string ConfirmationCode { get; set; }

        public IActionResult OnGet(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return RedirectToPage("/Index");
            }

            Email = email;
            return Page();
        }

        public async Task<IActionResult> OnPostConfirmEmailAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Find user by email
            var user = await _userManager.FindByEmailAsync(Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid email address.");
                return Page();
            }

            // Check if the confirmation code matches
            if (user.EmailConfirmationCode != ConfirmationCode)
            {
                ModelState.AddModelError(string.Empty, "Invalid confirmation code. Please check and try again.");
                return Page();
            }

            // Mark email as confirmed
            user.EmailConfirmed = true;
            
            // Clear the confirmation code
            user.EmailConfirmationCode = string.Empty;
            
            // Update the user
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Error confirming email. Please try again.");
                return Page();
            }

            // Sign in the user
            await _signInManager.SignInAsync(user, isPersistent: false);
            
            StatusMessage = "Thank you for confirming your email.";
            
            // Redirect based on user role
            if (user.UserRole == UserRoleType.LGU)
            {
                // Check if LGU user is approved before redirecting to dashboard
                if (user is LGUUser lguUser && !lguUser.IsApproved)
                {
                    await _signInManager.SignOutAsync();
                    StatusMessage = "Thank you for confirming your email. Your LGU account is pending approval. Please wait for administrator verification.";
                    return RedirectToPage("./PendingApproval");
                }
                return LocalRedirect("~/LGU/Dashboard");
            }
            else if (user.UserRole == UserRoleType.Community)
            {
                return LocalRedirect("~/Community/Dashboard");
            }
            
            // Default redirect if role is not set
            return LocalRedirect("~/");
        }
    }
}
