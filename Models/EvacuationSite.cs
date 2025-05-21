using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace THYNK.Models
{
    public class EvacuationSite
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Address { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string City { get; set; } = string.Empty;

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        [Required]
        public int Capacity { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        [Required]
        public DateTime DateAdded { get; set; } = DateTime.Now;

        public DateTime? LastUpdated { get; set; }

        [Required]
        public EvacuationSiteType Type { get; set; }

        // Additional details
        public string Description { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;

        // Facilities and accessibility features
        public bool HasWater { get; set; }
        public bool HasElectricity { get; set; }
        public bool HasMedicalSupplies { get; set; }
        public bool HasInternet { get; set; }
        public bool IsWheelchairAccessible { get; set; }
        public bool HasBathroomFacilities { get; set; }
        public bool HasKitchen { get; set; }
        public bool HasSleepingFacilities { get; set; }

        // Reference to the LGU user who created/manages the site
        [Required]
        public string ManagedByUserId { get; set; } = string.Empty;
        
        [ForeignKey("ManagedByUserId")]
        public ApplicationUser ManagedBy { get; set; } = null!;
    }

    public enum EvacuationSiteType
    {
        FloodEvacuation,
        FireEvacuation,
        EarthquakeEvacuation,
        TyphoonEvacuation,
        MultiHazard
    }
} 