using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class PatientNeed
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(200)]
        public string NeedName { get; set; } = string.Empty;

        public bool IsCompleted { get; set; } = false;

        public int? SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee? SocialWorker { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}