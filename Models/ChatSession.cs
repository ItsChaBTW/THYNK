using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public enum ChatStatus
    {
        Active,
        Closed,
        Pending
    }

    public class ChatSession
    {
        public int Id { get; set; }
        
        [Required]
        public string UserId { get; set; }
        
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }
        
        public string? Title { get; set; }
        
        [Required]
        public DateTime StartTime { get; set; } = DateTime.Now;
        
        public DateTime? EndTime { get; set; }
        
        [Required]
        public ChatStatus Status { get; set; } = ChatStatus.Active;
        
        public string? Category { get; set; }
        
        // For admin/LGU assignment - nullable
        public string? AssignedToId { get; set; }
        
        [ForeignKey("AssignedToId")]
        public ApplicationUser? AssignedTo { get; set; }
        
        // Navigation property
        public ICollection<ChatMessage> Messages { get; set; }
    }
} 