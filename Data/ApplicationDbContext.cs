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
    public DbSet<LGUUser> LGUUsers { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure discriminator values
        builder.Entity<ApplicationUser>()
            .HasDiscriminator<string>("Discriminator")
            .HasValue<ApplicationUser>("ApplicationUser")
            .HasValue<LGUUser>("LGUUser");

        // Configure LGUUser
        builder.Entity<LGUUser>()
            .HasBaseType<ApplicationUser>()
            .Property(u => u.Position)
            .IsRequired()
            .HasMaxLength(100);

        builder.Entity<LGUUser>()
            .Property(u => u.OrganizationName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Entity<LGUUser>()
            .Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Entity<LGUUser>()
            .Property(u => u.IDDocumentUrl)
            .HasMaxLength(500);
    }
}
