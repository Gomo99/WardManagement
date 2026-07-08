using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class AdmissionCondition
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int ConditionId { get; set; }
        [ForeignKey(nameof(ConditionId))]
        public Condition Condition { get; set; } = null!;
    }
}