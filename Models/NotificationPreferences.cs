using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class NotificationPreferences
    {
        public int Id { get; set; }
        
        [Required]
        public string UserId { get; set; }
        
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }
        
        public bool EmailEnabled { get; set; } = true;
        
        public bool SmsEnabled { get; set; } = false;
        
        public bool ReportUpdatesEnabled { get; set; } = true;
        
        public bool CommunityActivityEnabled { get; set; } = true;
    }
} 