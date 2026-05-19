using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class PatientMovement
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(20)]
        public string MovementType { get; set; } = null!;   // "CheckOutRequest", "CheckOut", "CheckIn", etc.

        [Required, StringLength(100)]
        public string Location { get; set; } = null!;

        [StringLength(200)]
        public string? Notes { get; set; }

        public DateTime? Timestamp { get; set; }   // null = pending, set when completed

        // Who performed the movement (nullable – only for actual moves)
        public int? PorterId { get; set; }
        [ForeignKey(nameof(PorterId))]
        public Employee? Porter { get; set; }
    }
}