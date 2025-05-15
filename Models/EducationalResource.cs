using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class EducationalResource
    {
        [Key]
        public required int Id { get; set; }

        [Required]
        [StringLength(100)]
        public required string Title { get; set; }

        [Required]
        public required string Description { get; set; }

        [Required]
        public required ResourceType Type { get; set; }

        // Content of the resource (for offline access)
        [Required]
        public required string Content { get; set; }

        // URL to external resource if applicable
        public required string ExternalUrl { get; set; }

        // File URL for downloadable resources
        public required string FileUrl { get; set; }

        // Size of downloadable resource in KB
        public int? FileSizeKB { get; set; }

        // Tags for categorization
        public required string Tags { get; set; }

        [Required]
        public required DateTime DateAdded { get; set; } = DateTime.Now;

        // Indicates if this resource is cacheable for offline access
        [Required]
        public required bool IsOfflineAccessible { get; set; } = false;
        
        // Approval status for resource moderation
        [Required]
        public ApprovalStatus ApprovalStatus { get; set; } = ApprovalStatus.Pending;
        
        // The user who created this resource
        public string? CreatedById { get; set; }
        
        [ForeignKey("CreatedById")]
        public LGUUser? CreatedBy { get; set; }
        
        // Date when the resource was approved
        public DateTime? ApprovedDate { get; set; }
        
        // Rejection reason if applicable
        public string? RejectionReason { get; set; }
        
        // Count how many times this resource has been viewed
        public int? ViewCount { get; set; } = 0;
    }

    public enum ResourceType
    {
        Guide,
        Tutorial,
        Infographic,
        Video,
        Document,
        FAQ,
        EmergencyContact
    }
    
    public enum ApprovalStatus
    {
        Pending,
        Approved,
        Rejected
    }
} 