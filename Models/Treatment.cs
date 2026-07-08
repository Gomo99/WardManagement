using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Treatment
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(100)]
        public string TreatmentType { get; set; } = null!;

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime TreatmentDate { get; set; } = DateTime.Now;

        public Status IsActive { get; set; } = Status.Active;
    }
}