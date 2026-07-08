namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class FollowUpRequestViewModel
    {
        public int AdmissionId { get; set; }
        public int? PreferredDoctorId { get; set; }
        public DateTime? PreferredDate { get; set; }
        public string? Reason { get; set; }
    }
}