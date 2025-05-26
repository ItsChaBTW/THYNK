using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class AlertReadStatus
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AlertId { get; set; }

        [Required]
        public string UserId { get; set; }

        [Required]
        public DateTime ReadAt { get; set; }

        // Navigation properties
        [ForeignKey("AlertId")]
        public virtual Alert Alert { get; set; }

        [ForeignKey("UserId")]
        public virtual ApplicationUser User { get; set; }
    }
} 