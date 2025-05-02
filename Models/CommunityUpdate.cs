using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class CommunityUpdate
    {
        [Key]
        public required int Id { get; set; }

        [Required]
        [StringLength(200)]
        public required string Content { get; set; }

        [Required]
        public required DateTime DatePosted { get; set; } = DateTime.Now;

        // Type of update
        [Required]
        public required UpdateType Type { get; set; }

        // Status of moderation
        [Required]
        public required ModerationStatus ModerationStatus { get; set; } = ModerationStatus.Pending;

        // Reference to the user who posted the update
        public required string UserId { get; set; }
        
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        // Optional location information
        public required string Location { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // Images or files related to the update
        public required string ImageUrl { get; set; }

        // If this is related to a disaster report
        public int? DisasterReportId { get; set; }
        
        [ForeignKey("DisasterReportId")]
        public DisasterReport? DisasterReport { get; set; }
    }

    public enum UpdateType
    {
        GeneralUpdate,
        HelpRequest,
        StatusUpdate,
        ResourceSharing,
        Information
    }

    public enum ModerationStatus
    {
        Pending,
        Approved,
        Rejected
    }
} 