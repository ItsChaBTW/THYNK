using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using THYNK.Models;

namespace THYNK.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<DisasterReport> DisasterReports { get; set; }
    public DbSet<CommunityUpdate> CommunityUpdates { get; set; }
    public DbSet<EducationalResource> EducationalResources { get; set; }
    public DbSet<Alert> Alerts { get; set; }
}
