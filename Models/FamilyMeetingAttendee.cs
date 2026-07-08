using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class FamilyMeetingAttendee
    {
        public int Id { get; set; }

        public int FamilyMeetingId { get; set; }
        [ForeignKey(nameof(FamilyMeetingId))]
        public FamilyMeeting FamilyMeeting { get; set; } = null!;

        public int EmployeeId { get; set; }
        [ForeignKey(nameof(EmployeeId))]
        public Employee Employee { get; set; } = null!;

        public bool HasAccepted { get; set; } = false;
    }
}