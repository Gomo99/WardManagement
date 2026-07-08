using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class FamilyMeeting
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        public DateTime ScheduledDate { get; set; }

        [StringLength(200)]
        public string? Location { get; set; }

        [StringLength(2000)]
        public string? Notes { get; set; }

        public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;

        public bool IsActive { get; set; } = true;

        public ICollection<FamilyMeetingAttendee> Attendees { get; set; } = new List<FamilyMeetingAttendee>();
    }
}