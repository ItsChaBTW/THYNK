using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class Alert
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        [Required]
        public AlertSeverity Severity { get; set; }

        [Required]
        public DateTime DateIssued { get; set; } = DateTime.Now;

        public DateTime? ExpiresAt { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        [Required]
        public string AffectedArea { get; set; } = string.Empty;

        // Image path for the alert
        public string ImagePath { get; set; } = string.Empty;

        // UI customization options
        public string BackgroundStyle { get; set; } = string.Empty;
        public string IconStyle { get; set; } = string.Empty;
        public string ColorScheme { get; set; } = string.Empty;

        // Reference to the LGU user who created the alert
        [Required]
        public string IssuedByUserId { get; set; } = string.Empty;
        
        [ForeignKey("IssuedByUserId")]
        public ApplicationUser User { get; set; } = null!;
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Danger,
        Critical
    }
} 