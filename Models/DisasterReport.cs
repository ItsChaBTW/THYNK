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

        // When the report was verified
        public DateTime? DateVerified { get; set; }
        
        // When the report was marked as in progress
        public DateTime? DateInProgress { get; set; }

        // Whether the report was submitted anonymously
        public bool IsAnonymous { get; set; } = false;

        [Required]
        public required string Location { get; set; }

        // Detailed location information
        public required string Purok { get; set; } = string.Empty;
        
        public required string Barangay { get; set; } = string.Empty;
        
        public required string City { get; set; } = string.Empty;
        
        public required string Country { get; set; } = string.Empty;

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
        public string UserId { get; set; } = string.Empty;
        
        [ForeignKey("UserId")]
        [System.Text.Json.Serialization.JsonIgnore]
        public ApplicationUser? User { get; set; }

        // Images or files related to the disaster
        public string ImageUrl { get; set; } = string.Empty;

        // Additional details
        public string? AdditionalInfo { get; set; }
        
        // Assignment information
        public string? AssignedToId { get; set; }
        
        [ForeignKey("AssignedToId")]
        [System.Text.Json.Serialization.JsonIgnore]
        public LGUUser? AssignedTo { get; set; }
        
        public DateTime? AssignedAt { get; set; }
        
        // When the report was resolved
        public DateTime? ResolvedAt { get; set; }

        // Resolution details
        public string? Resolution { get; set; }
        
        // Alias for ResolvedAt to maintain compatibility
        [NotMapped]
        public DateTime? DateResolved => ResolvedAt;
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