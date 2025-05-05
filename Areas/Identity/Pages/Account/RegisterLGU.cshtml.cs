using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using THYNK.Models;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace THYNK.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterLGUModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterLGUModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IWebHostEnvironment _environment;

        public RegisterLGUModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterLGUModel> logger,
            IEmailSender emailSender,
            RoleManager<IdentityRole> roleManager,
            IWebHostEnvironment environment)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _roleManager = roleManager;
            _environment = environment;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "First Name")]
            public string FirstName { get; set; }

            [Required]
            [Display(Name = "Last Name")]
            public string LastName { get; set; }

            [Required]
            [Display(Name = "Position / Designation")]
            public string Position { get; set; }

            [Required]
            [Display(Name = "Organization Name")]
            public string OrganizationName { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email Address")]
            public string Email { get; set; }

            [Required]
            [Display(Name = "Phone Number")]
            [RegularExpression(@"^9\d{9}$", ErrorMessage = "Phone number must start with 9 followed by 9 digits")]
            public string PhoneNumber { get; set; }

            [Display(Name = "Government ID or Office ID")]
            public IFormFile IDDocument { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/LGU/Dashboard");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            
            if (ModelState.IsValid)
            {
                var user = new LGUUser
                {
                    FirstName = Input.FirstName,
                    LastName = Input.LastName,
                    Position = Input.Position,
                    OrganizationName = Input.OrganizationName,
                    UserRole = UserRoleType.LGU
                };

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                user.PhoneNumber = "+63" + Input.PhoneNumber;

                // Handle ID document upload if provided
                if (Input.IDDocument != null)
                {
                    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "id_documents");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueFileName = $"{Guid.NewGuid()}_{Input.IDDocument.FileName}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await Input.IDDocument.CopyToAsync(fileStream);
                    }

                    user.IDDocumentUrl = $"/uploads/id_documents/{uniqueFileName}";
                }

                // Generate a 7-character random confirmation code
                user.EmailConfirmationCode = GenerateRandomCode(7);

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("LGU user created a new account with password.");

                    // Add user to LGU role
                    await _userManager.AddToRoleAsync(user, "LGU");
                    _logger.LogInformation("User added to LGU role.");

                    var userId = await _userManager.GetUserIdAsync(user);

                    // Send the confirmation code via email
                    await _emailSender.SendEmailAsync(
                        Input.Email,
                        "Confirm your email - THYNK",
                        $"Hello {user.FirstName},<br><br>Thank you for registering with THYNK as an LGU user. Please use the following code to confirm your email address:<br><br><strong>{user.EmailConfirmationCode}</strong><br><br>This code is valid for 24 hours.<br><br>If you did not register for THYNK, please ignore this email.<br><br>Thank you,<br>The THYNK Team");

                    // Redirect to email confirmation page
                    return RedirectToPage("ConfirmEmail", new { email = Input.Email });
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        private string GenerateRandomCode(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            var code = new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
            return code;
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
} 