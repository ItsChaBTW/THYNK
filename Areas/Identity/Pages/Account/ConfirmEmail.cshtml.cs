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
using THYNK.Services;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace THYNK.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<ConfirmEmailModel> _logger;

        public ConfirmEmailModel(
            UserManager<ApplicationUser> userManager, 
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender,
            ILogger<ConfirmEmailModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _logger = logger;
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
                ModelState.AddModelError(string.Empty, "The confirmation code you entered is incorrect. Please check your email and try again.");
                
                // Clear the input fields
                ConfirmationCode = string.Empty;
                
                // Add a note about resending
                ModelState.AddModelError(string.Empty, "If you're having trouble, you can request a new code using the 'Resend Code' button below.");
                
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
                // Set success message for community users
                StatusMessage = "Thank you for confirming your email! Your registration is now complete. You can now access your account.";
                return RedirectToPage("./RegistrationSuccess");
            }
            
            // Default redirect if role is not set
            return LocalRedirect("~/");
        }

        public async Task<IActionResult> OnPostResendCodeAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(Email))
                {
                    return new JsonResult(new { success = false, message = "Email address is required." });
                }

                var user = await _userManager.FindByEmailAsync(Email);
                if (user == null)
                {
                    return new JsonResult(new { success = false, message = "User not found." });
                }

                // Generate a new confirmation code
                var code = GenerateConfirmationCode();
                user.EmailConfirmationCode = code;
                
                // Update the user with the new code
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    return new JsonResult(new { success = false, message = $"Failed to update user: {errors}" });
                }

                // Send the new confirmation email
                var emailSent = await SendConfirmationEmailAsync(user, code);
                if (!emailSent)
                {
                    return new JsonResult(new { success = false, message = "Failed to send confirmation email. Please try again." });
                }

                return new JsonResult(new { success = true, message = "A new confirmation code has been sent to your email." });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "An error occurred while processing your request. Please try again." });
            }
        }

        private string GenerateConfirmationCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 7)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private async Task<bool> SendConfirmationEmailAsync(ApplicationUser user, string code)
        {
            try
            {
                _logger.LogInformation($"Attempting to send confirmation email to {user.Email} with code {code}");
                
                var emailBody = $@"
                    <h2>Email Confirmation Code</h2>
                    <p>Hello {user.FirstName},</p>
                    <p>Your new confirmation code is: <strong>{code}</strong></p>
                    <p>Please use this code to verify your email address.</p>
                    <p>If you did not request this code, please ignore this email.</p>";

                await _emailSender.SendEmailAsync(
                    user.Email,
                    "Email Confirmation Code",
                    emailBody);

                _logger.LogInformation($"Successfully sent confirmation email to {user.Email}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send confirmation email to {user.Email}: {ex.Message}");
                _logger.LogError($"Exception details: {ex}");
                return false;
            }
        }
    }
}
