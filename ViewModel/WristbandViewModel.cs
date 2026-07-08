namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class WristbandViewModel
    {
        public string PatientName { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public int AdmissionId { get; set; }
        public string BedNumber { get; set; } = string.Empty;
        public string WardName { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
        public string BarcodeBase64 { get; set; } = string.Empty;
    }
}