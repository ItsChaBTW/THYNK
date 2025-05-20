using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class UserNotification
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string UserId { get; set; }
        
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }
        
        [Required]
        public string Title { get; set; }
        
        [Required]
        public string Message { get; set; }
        
        public string NotificationType { get; set; } // success, warning, info, etc.
        
        public int? RelatedEntityId { get; set; } // ID of related report, post, etc.
        
        public string RelatedEntityType { get; set; } // Type of entity: Report, Post, etc.
        
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public bool IsRead { get; set; } = false;
    }
} 