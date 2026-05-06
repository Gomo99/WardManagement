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
        public string? Dosage { get; set; }         // e.g. "500mg"

        [StringLength(100)]
        public string? Frequency { get; set; }      // e.g. "Twice daily"

        [StringLength(50)]
        public string? Duration { get; set; }       // e.g. "7 days"

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime PrescribedDate { get; set; } = DateTime.Now;

        public Status IsActive { get; set; } = Status.Active;

        public ScriptStatus ScriptStatus { get; set; } = ScriptStatus.New;


        public int? ScriptManagerId { get; set; }
        [ForeignKey(nameof(ScriptManagerId))]
        public Employee? ScriptManager { get; set; }
    }
}