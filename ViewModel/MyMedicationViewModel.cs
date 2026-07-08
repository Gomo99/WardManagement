namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class MyMedicationViewModel
    {
        public string MedicationName { get; set; } = string.Empty;
        public string? Dosage { get; set; }
        public string? Frequency { get; set; }
        public string? Duration { get; set; }
        public DateTime? PrescribedDate { get; set; }
        public DateTime? LastAdministered { get; set; }
    }
}