using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        
        [Required]
        public int ChatSessionId { get; set; }
        
        [ForeignKey("ChatSessionId")]
        public ChatSession ChatSession { get; set; }
        
        public string? SenderId { get; set; }
        
        [ForeignKey("SenderId")]
        public ApplicationUser? Sender { get; set; }
        
        [Required]
        public string Content { get; set; }
        
        public string? AttachmentUrl { get; set; }
        
        [Required]
        public DateTime Timestamp { get; set; } = DateTime.Now;
        
        public bool IsRead { get; set; } = false;
    }
} 