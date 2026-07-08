namespace WARDMANAGEMENTSYSTEM.Models
{
    public class AllergyMedication
    {
        public int Id { get; set; }

        public int MedicationId { get; set; }
        public Medication Medication { get; set; } = null!;

        public int AllergyId { get; set; }
        public Allergy Allergy { get; set; } = null!;
    }
}