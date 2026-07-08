namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class PatientAppointmentViewModel
    {
        public int VisitId { get; set; }
        public int AdmissionId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public DateTime? VisitDate { get; set; }
        public string? Notes { get; set; }
    }
}