using System.ComponentModel.DataAnnotations;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Medication
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(200)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string? DosageForm { get; set; }  // e.g., Tablet, Injection, Syrup

        public int? Schedule { get; set; }   // 1-8, higher = more controlled
        public Status IsActive { get; set; } = Status.Active;


    }
}