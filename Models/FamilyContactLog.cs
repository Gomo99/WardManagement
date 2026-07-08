using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class FamilyContactLog
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(100)]
        public string ContactName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Relationship { get; set; }

        public DateTime ContactDate { get; set; } = DateTime.Now;

        [StringLength(2000)]
        public string? Notes { get; set; }

        [StringLength(2000)]
        public string? DecisionsMade { get; set; }

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool IsActive { get; set; } = true;
    }
}