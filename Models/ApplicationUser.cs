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

        // Address fields
        public string RegionCode { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public string ProvinceCode { get; set; } = string.Empty;
        public string ProvinceName { get; set; } = string.Empty;
        public string CityMunicipalityCode { get; set; } = string.Empty;
        public string CityMunicipalityName { get; set; } = string.Empty;
        public string BarangayCode { get; set; } = string.Empty;
        public string BarangayName { get; set; } = string.Empty;
    }
    
    public enum UserRoleType
    {
        Community,
        LGU,
        Admin
    }
} 