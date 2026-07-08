using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class PsychosocialAssessment
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        [StringLength(1000)]
        public string? SocialHistory { get; set; }

        [StringLength(1000)]
        public string? SupportNetwork { get; set; }

        [StringLength(1000)]
        public string? FinancialConcerns { get; set; }

        [StringLength(1000)]
        public string? SubstanceUse { get; set; }

        [StringLength(1000)]
        public string? MentalHealthStatus { get; set; }

        [StringLength(2000)]
        public string? AdditionalNotes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; } = true;
    }
}