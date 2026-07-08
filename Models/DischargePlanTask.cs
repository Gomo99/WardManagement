using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class DischargePlanTask
    {
        public int Id { get; set; }

        public int DischargePlanId { get; set; }
        [ForeignKey(nameof(DischargePlanId))]
        public DischargePlan DischargePlan { get; set; } = null!;

        [Required, StringLength(200)]
        public string TaskName { get; set; } = string.Empty;

        public DateTime? DueDate { get; set; }

        public bool IsCompleted { get; set; } = false;

        public int? CompletedBySocialWorkerId { get; set; }
        [ForeignKey(nameof(CompletedBySocialWorkerId))]
        public Employee? CompletedBySocialWorker { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool IsActive { get; set; } = true;
    }
}