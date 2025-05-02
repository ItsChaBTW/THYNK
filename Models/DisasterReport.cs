using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class DisasterReport
    {
        [Key]
        public required int Id { get; set; }

        [Required]
        [StringLength(100)]
        public required string Title { get; set; }

        [Required]
        public required string Description { get; set; }

        [Required]
        public required DisasterType Type { get; set; }

        [Required]
        public required DateTime DateReported { get; set; } = DateTime.Now;

        [Required]
        public required string Location { get; set; }

        [Required]
        public required double Latitude { get; set; }

        [Required]
        public required double Longitude { get; set; }

        // Severity level
        [Required]
        public required SeverityLevel Severity { get; set; }

        // Status of the report
        [Required]
        public required ReportStatus Status { get; set; } = ReportStatus.Pending;

        // Reference to the user who reported the disaster
        public required string UserId { get; set; }
        
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        // Images or files related to the disaster
        public required string ImageUrl { get; set; }

        // Additional details
        public required string AdditionalInfo { get; set; }
    }

    public enum DisasterType
    {
        Earthquake,
        Flood,
        Fire,
        Landslide,
        Storm,
        Accident,
        Other
    }

    public enum SeverityLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum ReportStatus
    {
        Pending,
        Verified,
        InProgress,
        Resolved,
        Declined
    }
} 