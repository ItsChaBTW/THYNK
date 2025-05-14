// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using THYNK.Models;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace THYNK.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public IndexModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _webHostEnvironment = webHostEnvironment;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        ///     Current profile photo URL
        /// </summary>
        public string CurrentProfilePhotoUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string StatusMessage { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            [Required]
            [Display(Name = "First Name")]
            public string FirstName { get; set; }

            [Required]
            [Display(Name = "Last Name")]
            public string LastName { get; set; }

            [Display(Name = "Bio")]
            [StringLength(500, ErrorMessage = "The {0} must be at most {1} characters long.")]
            public string Bio { get; set; }

            [Phone]
            [Display(Name = "Phone Number")]
            public string PhoneNumber { get; set; }

            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Display(Name = "Profile Photo")]
            public IFormFile ProfilePhoto { get; set; }
            
            [Display(Name = "Province")]
            public string ProvinceName { get; set; }
            
            [Display(Name = "City/Municipality")]
            public string CityMunicipalityName { get; set; }
            
            [Display(Name = "Barangay")]
            public string BarangayName { get; set; }
        }

        private async Task LoadAsync(ApplicationUser user)
        {
            var userName = await _userManager.GetUserNameAsync(user);
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            var email = await _userManager.GetEmailAsync(user);

            Username = userName;
            CurrentProfilePhotoUrl = user.ProfilePhotoUrl;

            Input = new InputModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Bio = user.Bio,
                PhoneNumber = phoneNumber,
                Email = email,
                ProvinceName = user.ProvinceName,
                CityMunicipalityName = user.CityMunicipalityName,
                BarangayName = user.BarangayName
            };
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            if (Input.PhoneNumber != phoneNumber)
            {
                var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
                if (!setPhoneResult.Succeeded)
                {
                    StatusMessage = "Unexpected error when trying to set phone number.";
                    return RedirectToPage();
                }
            }

            // Update user properties
            user.FirstName = Input.FirstName;
            user.LastName = Input.LastName;
            user.Bio = Input.Bio;
            user.ProvinceName = Input.ProvinceName;
            user.CityMunicipalityName = Input.CityMunicipalityName;
            user.BarangayName = Input.BarangayName;

            // Handle profile photo upload
            if (Input.ProfilePhoto != null && Input.ProfilePhoto.Length > 0)
            {
                // Check file size (max 2MB)
                if (Input.ProfilePhoto.Length > 2 * 1024 * 1024)
                {
                    ModelState.AddModelError("Input.ProfilePhoto", "Profile photo must be less than 2MB.");
                    await LoadAsync(user);
                    return Page();
                }
                
                // Check file type
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif" };
                if (!allowedTypes.Contains(Input.ProfilePhoto.ContentType.ToLower()))
                {
                    ModelState.AddModelError("Input.ProfilePhoto", "Only JPEG, PNG, and GIF images are allowed.");
                    await LoadAsync(user);
                    return Page();
                }
                
                try
                {
                    // Create directory if it doesn't exist
                    var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "profile_pics");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    
                    // Generate unique filename
                    var userId = await _userManager.GetUserIdAsync(user);
                    var uniqueFileName = $"{userId}_{DateTime.Now.Ticks}{Path.GetExtension(Input.ProfilePhoto.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    
                    // Save file to disk
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await Input.ProfilePhoto.CopyToAsync(fileStream);
                    }
                    
                    // Set ImageUrl property
                    user.ProfilePhotoUrl = $"/uploads/profile_pics/{uniqueFileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error uploading profile photo: {ex.Message}";
                    return RedirectToPage();
                }
            }

            // Save all changes
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                StatusMessage = "Unexpected error when trying to update your profile information.";
                return RedirectToPage();
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Your profile has been updated";
            return RedirectToPage();
        }
    }
}
