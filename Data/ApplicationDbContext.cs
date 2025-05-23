﻿using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
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
    public DbSet<EvacuationSite> EvacuationSites { get; set; }
    public DbSet<LGUUser> LGUUsers { get; set; }
    public DbSet<FAQ> FAQs { get; set; }
    public DbSet<ChatSession> ChatSessions { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<SupportChat> SupportChats { get; set; }
    public DbSet<NotificationPreferences> NotificationPreferences { get; set; }
    public DbSet<UserNotification> UserNotifications { get; set; }

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
            
        // Configure ChatSession relationships
        builder.Entity<ChatSession>()
            .HasMany(c => c.Messages)
            .WithOne(m => m.ChatSession)
            .HasForeignKey(m => m.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);
            
        // Fix cascade delete paths to avoid cycles
        builder.Entity<ChatSession>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.NoAction);
            
        builder.Entity<ChatSession>()
            .HasOne(c => c.AssignedTo)
            .WithMany()
            .HasForeignKey(c => c.AssignedToId)
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired(false);

        // Configure SupportChat relationships
        builder.Entity<SupportChat>()
            .HasOne(sc => sc.User)
            .WithMany()
            .HasForeignKey(sc => sc.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<SupportChat>()
            .HasOne(sc => sc.AssignedTo)
            .WithMany()
            .HasForeignKey(sc => sc.AssignedToId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<SupportChat>()
            .HasOne(sc => sc.ResolvedBy)
            .WithMany()
            .HasForeignKey(sc => sc.ResolvedById)
            .OnDelete(DeleteBehavior.NoAction);

        // Make some fields optional
        builder.Entity<SupportChat>()
            .Property(sc => sc.AssignedToId)
            .IsRequired(false);

        builder.Entity<SupportChat>()
            .Property(sc => sc.ResolvedById)
            .IsRequired(false);

        builder.Entity<SupportChat>()
            .Property(sc => sc.Resolution)
            .IsRequired(false);

        builder.Entity<SupportChat>()
            .Property(sc => sc.Category)
            .IsRequired(false);
    }
}
