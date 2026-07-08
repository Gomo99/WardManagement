using System.ComponentModel.DataAnnotations;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class EmergencyContact
    {
        public int Id { get; set; }

        [Required]
        public int PatientId { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string Relationship { get; set; } = string.Empty;

        [Required, StringLength(20)]
        public string Phone { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Notes { get; set; }

        public Patient? Patient { get; set; }
    }
}