using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class PatientMovement
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(20)]
        public string MovementType { get; set; } = null!;   // "CheckOut" or "CheckIn"

        [Required, StringLength(100)]
        public string Location { get; set; } = null!;       // e.g. "Theatre", "X-Ray", "Physio"

        [StringLength(200)]
        public string? Notes { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}