namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class PatientLoginCardViewModel
    {
        public string PatientName { get; set; } = string.Empty;
        public int PatientId { get; set; }
        public string PortalUrl { get; set; } = string.Empty;
        public string LoginEmail { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
    }
}