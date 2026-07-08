namespace WARDMANAGEMENTSYSTEM.Models
{
    public class ConditionMedication
    {
        public int Id { get; set; }

        public int MedicationId { get; set; }
        public Medication Medication { get; set; } = null!;

        public int ConditionId { get; set; }
        public Condition Condition { get; set; } = null!;
    }
}