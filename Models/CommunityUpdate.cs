using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class CommunityUpdate
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(2000)]
        public string Content { get; set; }

        public DateTime DatePosted { get; set; }

        [Required]
        public UpdateType Type { get; set; }

        public ModerationStatus ModerationStatus { get; set; }

        public string UserId { get; set; }
        
        [ForeignKey("UserId")]
        public virtual ApplicationUser? User { get; set; }

        public string? Location { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public string? ImageUrl { get; set; }
        public int? DisasterReportId { get; set; }
        
        [ForeignKey("DisasterReportId")]
        public virtual DisasterReport? DisasterReport { get; set; }
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