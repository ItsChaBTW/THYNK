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
                await roleManager.CreateAsync(new IdentityRole("Community"));
                await roleManager.CreateAsync(new IdentityRole("LGU"));
            }
            // If roles exist but Community role doesn't exist
            else if (!await roleManager.RoleExistsAsync("Community"))
            {
                await roleManager.CreateAsync(new IdentityRole("Community"));
            }
            // If roles exist but LGU role doesn't exist
            else if (!await roleManager.RoleExistsAsync("LGU"))
            {
                await roleManager.CreateAsync(new IdentityRole("LGU"));
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
                    LastName = "User",
                    UserRole = UserRoleType.Admin
                };

                var result = await userManager.CreateAsync(adminUser, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }

            // Check if community user exists
            if (!userManager.Users.Any(u => u.UserName == "community@thynk.com"))
            {
                // Create community user
                var communityUser = new ApplicationUser
                {
                    UserName = "community@thynk.com",
                    Email = "community@thynk.com",
                    EmailConfirmed = true,
                    FirstName = "Community",
                    LastName = "User",
                    UserRole = UserRoleType.Community
                };

                var result = await userManager.CreateAsync(communityUser, "Community@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(communityUser, "Community");
                }
            }
            
            // Check if LGU user exists
            if (!userManager.Users.Any(u => u.UserName == "lgu@thynk.com"))
            {
                // Create LGU user
                var lguUser = new ApplicationUser
                {
                    UserName = "lgu@thynk.com",
                    Email = "lgu@thynk.com",
                    EmailConfirmed = true,
                    FirstName = "LGU",
                    LastName = "User",
                    UserRole = UserRoleType.LGU
                };

                var result = await userManager.CreateAsync(lguUser, "Lgu@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(lguUser, "LGU");
                }
            }

            await context.SaveChangesAsync();
        }
    }
} 