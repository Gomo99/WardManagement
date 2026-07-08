using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class DoctorVisit
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int? DoctorId { get; set; }
        [ForeignKey(nameof(DoctorId))]
        public Employee? Doctor { get; set; }

        [StringLength(100)]
        public string? ExternalDoctorName { get; set; }

        [Required]
        public DateTime VisitDate { get; set; } = DateTime.Now;

        [StringLength(1000)]
        public string? Instructions { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public bool IsContactRecord { get; set; } = false;

        public Status IsActive { get; set; } = Status.Active;
    }
}