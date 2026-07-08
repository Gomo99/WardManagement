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
        public string MovementType { get; set; } = null!;

        [Required, StringLength(100)]
        public string Location { get; set; } = null!;

        [StringLength(200)]
        public string? Notes { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public DateTime? Timestamp { get; set; }

        public int? PorterId { get; set; }
        [ForeignKey(nameof(PorterId))]
        public Employee? Porter { get; set; }

        public DateTime? RejectedAt { get; set; }
        [StringLength(200)]
        public string? RejectionReason { get; set; }
        public DateTime? ETA { get; set; }
        public int? RequestedByWardAdminId { get; set; }
    }
}