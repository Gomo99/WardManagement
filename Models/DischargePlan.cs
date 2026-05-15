using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class DischargePlan
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        [Required, StringLength(2000)]
        public string PlanDetails { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Status IsActive { get; set; } = Status.Active;
    }
}