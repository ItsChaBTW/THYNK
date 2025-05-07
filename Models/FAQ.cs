using System.ComponentModel.DataAnnotations;

namespace THYNK.Models
{
    public class FAQ
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Question { get; set; }
        
        [Required]
        public string Answer { get; set; }
        
        [StringLength(50)]
        public string Category { get; set; }

        public int DisplayOrder { get; set; }
        
        public bool IsPublished { get; set; } = true;
    }
} 