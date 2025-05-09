using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace THYNK.Models
{
    public class SupportChat
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string? UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string Description { get; set; }

        public string Status { get; set; } = "Open"; // Open, InProgress, Closed

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? LastUpdated { get; set; }

        public string AssignedToId { get; set; }

        [ForeignKey("AssignedToId")]
        public ApplicationUser AssignedTo { get; set; }

        public string Priority { get; set; } = "Normal"; // Low, Normal, High, Urgent

        public string Category { get; set; }

        public bool IsResolved { get; set; } = false;

        public string Resolution { get; set; }

        public DateTime? ResolvedAt { get; set; }

        public string? ResolvedById { get; set; }

        [ForeignKey("ResolvedById")]
        public ApplicationUser ResolvedBy { get; set; }
    }
} 