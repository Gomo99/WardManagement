using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Referral
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(200)]
        public string OrganisationName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ContactPerson { get; set; }

        [StringLength(20)]
        public string? ContactPhone { get; set; }

        [StringLength(200)]
        public string? ContactEmail { get; set; }

        [StringLength(1000)]
        public string? Reason { get; set; }

        public DateTime DateReferral { get; set; } = DateTime.Now;

        public ReferralOutcome Outcome { get; set; } = ReferralOutcome.Pending;

        [StringLength(1000)]
        public string? OutcomeNotes { get; set; }

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        public bool IsActive { get; set; } = true;
    }
}