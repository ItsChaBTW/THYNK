using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class ReportRating
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ReportId { get; set; }

        [ForeignKey("ReportId")]
        public DisasterReport Report { get; set; }

        [Required]
        [Range(1, 5)]
        public int Rating { get; set; }

        public string Feedback { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }
    }
} 