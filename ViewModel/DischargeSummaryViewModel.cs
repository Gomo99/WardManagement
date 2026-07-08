namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class DischargeSummaryViewModel
    {
        public string PatientName { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public DateTime? AdmissionDate { get; set; }
        public DateTime? DischargeDate { get; set; }
        public string? WardName { get; set; }
        public string? BedNumber { get; set; }
        public string? DoctorName { get; set; }
        public List<string> Allergies { get; set; } = new();
        public List<string> Conditions { get; set; } = new();
        public List<TreatmentSummary> Treatments { get; set; } = new();
        public List<MedSummary> Medications { get; set; } = new();
        public List<string?> FollowUpInstructions { get; set; } = new();
    }
}