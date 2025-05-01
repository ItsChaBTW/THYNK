using Microsoft.AspNetCore.Identity;
using THYNK.Models;

namespace THYNK.Data
{
    public static class DbInitializer
    {
        public static async Task Initialize(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            // Ensure database is created
            context.Database.EnsureCreated();

            // Check if roles exist
            if (!roleManager.Roles.Any())
            {
                // Create roles
                await roleManager.CreateAsync(new IdentityRole("Admin"));
                await roleManager.CreateAsync(new IdentityRole("User"));
            }

            // Check if admin user exists
            if (!userManager.Users.Any(u => u.UserName == "admin@thynk.com"))
            {
                // Create admin user
                var adminUser = new ApplicationUser
                {
                    UserName = "admin@thynk.com",
                    Email = "admin@thynk.com",
                    EmailConfirmed = true,
                    FirstName = "Admin",
                    LastName = "User"
                };

                var result = await userManager.CreateAsync(adminUser, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }

            await context.SaveChangesAsync();
        }
    }
} 