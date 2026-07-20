using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Prescription
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int MedicationId { get; set; }
        [ForeignKey(nameof(MedicationId))]
        public Medication Medication { get; set; } = null!;

        [StringLength(100)]
        public string? Dosage { get; set; }

        [StringLength(100)]
        public string? Frequency { get; set; }

        [StringLength(50)]
        public string? Duration { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime PrescribedDate { get; set; } = DateTime.Now;

        public Status IsActive { get; set; } = Status.Active;

        public ScriptStatus ScriptStatus { get; set; } = ScriptStatus.New;
        public int? PharmacistId { get; set; }
        [ForeignKey(nameof(PharmacistId))]
        public Employee? Pharmacist { get; set; }

        public int? ScriptManagerId { get; set; }
        [ForeignKey(nameof(ScriptManagerId))]
        public Employee? ScriptManager { get; set; }

        public bool IsStat { get; set; } = false;

        public DateTime? DeliveredAt { get; set; }

        // Add this inside your Prescription class:
        // In Prescription.cs, add:
        public DateTime? ForwardedAt { get; set; }
        public int? ForwardedByScriptManagerId { get; set; }

        // Verification fields
        public DateTime? ExpiryDate { get; set; }
        [StringLength(50)]
        public string? BatchNumber { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public int? VerifiedByEmployeeId { get; set; }
        [ForeignKey(nameof(VerifiedByEmployeeId))]
        public Employee? VerifiedBy { get; set; }

        public int QuantityPrescribed { get; set; } = 1;   // default to 1 if not specified
        public int QuantityReceived { get; set; } = 0;

        public PrescriptionPriority Priority { get; set; } = PrescriptionPriority.Normal;
    }
}