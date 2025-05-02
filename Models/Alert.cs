using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class Alert
    {
        [Key]
        public required int Id { get; set; }

        [Required]
        [StringLength(100)]
        public required string Title { get; set; }

        [Required]
        public required string Message { get; set; }

        [Required]
        public required AlertType Type { get; set; }

        [Required]
        public required AlertSeverity Severity { get; set; }

        [Required]
        public required DateTime DateIssued { get; set; } = DateTime.Now;

        // Expiration date for the alert
        public DateTime? ExpiresAt { get; set; }

        // Affected area
        public required string AffectedArea { get; set; }

        // Geographic boundaries for the affected area
        public double? CenterLatitude { get; set; }
        public double? CenterLongitude { get; set; }
        public double? RadiusKm { get; set; }

        // Related disaster report if applicable
        public int? DisasterReportId { get; set; }
        
        [ForeignKey("DisasterReportId")]
        public DisasterReport? DisasterReport { get; set; }

        // User who issued the alert (usually an LGU or admin)
        public required string IssuedByUserId { get; set; }
        
        [ForeignKey("IssuedByUserId")]
        public ApplicationUser IssuedByUser { get; set; }

        // Additional instructions or resources
        public required string Instructions { get; set; }
        
        // Flag to indicate if the alert is active
        [Required]
        public required bool IsActive { get; set; } = true;
    }

    public enum AlertType
    {
        Weather,
        Earthquake,
        Flood,
        Fire,
        Landslide,
        Traffic,
        Health,
        Security,
        General
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Danger,
        Critical
    }
} 