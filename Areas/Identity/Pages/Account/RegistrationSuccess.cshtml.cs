using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace THYNK.Areas.Identity.Pages.Account
{
    public class RegistrationSuccessModel : PageModel
    {
        [TempData]
        public string StatusMessage { get; set; }

        public IActionResult OnGet()
        {
            if (string.IsNullOrEmpty(StatusMessage))
            {
                return RedirectToPage("/Index");
            }

            return Page();
        }
    }
}