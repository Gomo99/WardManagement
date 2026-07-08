namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class PatientInstructionViewModel
    {
        public DateTime VisitDate { get; set; }
        public string Instructions { get; set; }
        public string DoctorName { get; set; }
        public int? AdmissionId { get; set; }
    }
}