using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace THYNK.Models
{
    public class LGUUser : ApplicationUser
    {
        [Required]
        [StringLength(100)]
        public required string Position { get; set; }

        [Required]
        [StringLength(100)]
        public required string OrganizationName { get; set; }

        public string? IDDocumentUrl { get; set; }

        [NotMapped]
        public IFormFile? IDDocument { get; set; }
        
        // Approval status
        public bool IsApproved { get; set; } = false;
    }
} 