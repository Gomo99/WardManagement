using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class SocialWorkReportViewModel
    {
        public string PatientName { get; set; } = string.Empty;
        public string? DateOfBirth { get; set; }
        public string? AdmissionDate { get; set; }
        public string? DischargeDate { get; set; }
        public string? DoctorName { get; set; }
        public string? WardName { get; set; }

        public PsychosocialAssessment? Assessment { get; set; }

        public List<DischargePlan> DischargePlans { get; set; } = new();
        public List<DischargePlanTask> AllPlanTasks { get; set; } = new();

        public List<RiskScreening> RiskScreenings { get; set; } = new();

        public List<Referral> Referrals { get; set; } = new();

        public List<PatientNeed> NeedsChecklist { get; set; } = new();

        public List<FollowUp> FollowUps { get; set; } = new();

        public List<FamilyContactLog> FamilyContacts { get; set; } = new();

        public List<FamilyMeeting> Meetings { get; set; } = new();
    }
}