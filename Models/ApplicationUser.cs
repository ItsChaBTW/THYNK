using Microsoft.AspNetCore.Identity;

namespace THYNK.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; } = DateTime.Now;
        
        // User role type (Community, LGU, Admin)
        public UserRoleType UserRole { get; set; } = UserRoleType.Community;
        
        // Email confirmation code
        public string EmailConfirmationCode { get; set; } = string.Empty;
    }
    
    public enum UserRoleType
    {
        Community,
        LGU,
        Admin
    }
} 