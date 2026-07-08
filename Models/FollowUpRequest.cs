using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class FollowUpRequest
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        [ForeignKey(nameof(PatientId))]
        public Patient Patient { get; set; } = null!;

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int? PreferredDoctorId { get; set; }
        [ForeignKey(nameof(PreferredDoctorId))]
        public Employee? PreferredDoctor { get; set; }

        public DateTime? PreferredDate { get; set; }
        public string? Reason { get; set; }
        public DateTime RequestDate { get; set; }
        public FollowUpRequestStatus Status { get; set; }
        public Status IsActive { get; set; }
    }
}