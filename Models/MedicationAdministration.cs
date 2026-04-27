using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class MedicationAdministration
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int MedicationId { get; set; }
        [ForeignKey(nameof(MedicationId))]
        public Medication Medication { get; set; } = null!;

        [StringLength(100)]
        public string? Dosage { get; set; }   // e.g., "500mg"

        public DateTime DateAdministered { get; set; } = DateTime.Now;

        [StringLength(200)]
        public string? Notes { get; set; }

        public Status IsActive { get; set; } = Status.Active;
    }
}