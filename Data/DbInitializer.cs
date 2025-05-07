using Microsoft.AspNetCore.Identity;
using THYNK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

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

            // Seed FAQs if none exist
            if (!context.FAQs.Any())
            {
                var faqs = new List<FAQ>
                {
                    // General Usage
                    new FAQ 
                    { 
                        Question = "What is THYNK?", 
                        Answer = "<p>THYNK (Tactical Hazard Yielding Network for Knowledge) is a community-based disaster and accident reporting system designed to facilitate real-time alerts and coordination between local residents and government agencies during emergencies and disasters.</p>", 
                        Category = "General Usage",
                        DisplayOrder = 1,
                        IsPublished = true
                    },
                    new FAQ 
                    { 
                        Question = "How do I create an account?", 
                        Answer = "<p>To create a community member account:</p><ol><li>Click on the \"Register\" button on the login page</li><li>Fill in your personal details</li><li>Verify your email address</li><li>Log in with your new credentials</li></ol>", 
                        Category = "General Usage",
                        DisplayOrder = 2,
                        IsPublished = true
                    },
                    new FAQ 
                    { 
                        Question = "How do I navigate the dashboard?", 
                        Answer = "<p>The dashboard provides an overview of key information:</p><ul><li>Quick action buttons are at the top</li><li>Recent alerts and reports are displayed in cards</li><li>The sidebar menu provides access to all features</li><li>Your profile and account settings are accessible from the top-right corner</li></ul>", 
                        Category = "General Usage",
                        DisplayOrder = 3,
                        IsPublished = true
                    },
                    
                    // Reporting Incidents
                    new FAQ 
                    { 
                        Question = "How do I report a disaster or incident?", 
                        Answer = "<p>To report an incident:</p><ol><li>Click on \"Report Incident\" from the dashboard or sidebar</li><li>Fill in the incident details including type, severity, and location</li><li>Add any photos if available</li><li>Submit the report</li></ol><p>Your report will be reviewed by moderators before being visible to all users.</p>", 
                        Category = "Reporting Incidents",
                        DisplayOrder = 1,
                        IsPublished = true
                    },
                    new FAQ 
                    { 
                        Question = "What information should I include in my report?", 
                        Answer = "<p>Please include as much relevant information as possible:</p><ul><li>Accurate location details</li><li>Type of incident (flood, fire, earthquake, etc.)</li><li>Severity level</li><li>Number of people affected (if known)</li><li>Any immediate dangers</li><li>Photos of the incident (if safe to capture)</li><li>Any emergency services already on scene</li></ul>", 
                        Category = "Reporting Incidents",
                        DisplayOrder = 2,
                        IsPublished = true
                    },
                    new FAQ 
                    { 
                        Question = "How is my location determined when reporting?", 
                        Answer = "<p>You can set your location in three ways:</p><ol><li>Use the \"Detect Location\" button to automatically capture your current GPS coordinates</li><li>Manually enter your address details</li><li>Place a pin on the map by clicking at the exact location</li></ol><p>For the most accurate reporting, please ensure your device's location services are enabled.</p>", 
                        Category = "Reporting Incidents",
                        DisplayOrder = 3,
                        IsPublished = true
                    },
                    
                    // Alerts and Notifications
                    new FAQ 
                    { 
                        Question = "How do I receive alerts about nearby incidents?", 
                        Answer = "<p>Alerts are sent automatically based on your location settings:</p><ul><li>Enable notifications in your account settings</li><li>Set your home location and areas of interest</li><li>Choose which types of incidents you want to be notified about</li><li>Select your preferred notification methods (in-app, email, SMS)</li></ul>", 
                        Category = "Alerts and Notifications",
                        DisplayOrder = 1,
                        IsPublished = true
                    },
                    new FAQ 
                    { 
                        Question = "What do the different alert severity levels mean?", 
                        Answer = "<p>Alert severity levels indicate the urgency and potential impact:</p><ul><li><strong>Low</strong>: Minor incident with limited impact</li><li><strong>Medium</strong>: Moderate incident affecting a neighborhood area</li><li><strong>High</strong>: Serious incident with significant community impact</li><li><strong>Critical</strong>: Major emergency requiring immediate action</li></ul>", 
                        Category = "Alerts and Notifications",
                        DisplayOrder = 2,
                        IsPublished = true
                    },
                    
                    // LGU/SLU Features
                    new FAQ 
                    { 
                        Question = "How do I register as an LGU/SLU representative?", 
                        Answer = "<p>To register as a Local Government Unit (LGU) or Special LGU (SLU) representative:</p><ol><li>Click \"Register as LGU/SLU\" on the login page</li><li>Fill in your personal and organizational details</li><li>Upload verification documents</li><li>Submit your application</li></ol><p>An administrator will review and approve your application, typically within 1-2 business days.</p>", 
                        Category = "LGU/SLU Features",
                        DisplayOrder = 1,
                        IsPublished = true
                    },
                    new FAQ 
                    { 
                        Question = "What special features are available for LGU/SLU users?", 
                        Answer = "<p>LGU/SLU users have access to additional features:</p><ul><li>Incident verification and status management</li><li>Ability to issue official alerts</li><li>Response coordination tools</li><li>Dashboard with analytics and reporting</li><li>Communication channels with community members</li><li>Resource management tools</li></ul>", 
                        Category = "LGU/SLU Features",
                        DisplayOrder = 2,
                        IsPublished = true
                    },
                    
                    // Technical Support
                    new FAQ 
                    { 
                        Question = "How do I report a technical issue with the platform?", 
                        Answer = "<p>To report technical issues:</p><ol><li>Go to the Support page from the sidebar menu</li><li>Click \"Start Chat\" and select \"Technical Issue\" as the category</li><li>Describe the problem in detail, including what you were doing when it occurred</li><li>Include any error messages, your device type, and browser information</li></ol><p>Our technical team will respond as soon as possible.</p>", 
                        Category = "Technical Support",
                        DisplayOrder = 1,
                        IsPublished = true
                    },
                    new FAQ 
                    { 
                        Question = "Can I use THYNK offline during a disaster?", 
                        Answer = "<p>Limited offline functionality is available:</p><ul><li>Previously loaded maps can be viewed offline</li><li>You can prepare reports offline that will be submitted when connectivity is restored</li><li>Downloaded educational resources remain accessible</li><li>Cached alerts will still be viewable</li></ul><p>We recommend downloading essential information in advance if you anticipate connectivity issues.</p>", 
                        Category = "Technical Support",
                        DisplayOrder = 2,
                        IsPublished = true
                    }
                };
                
                context.FAQs.AddRange(faqs);
            }

            await context.SaveChangesAsync();
        }
    }
} 