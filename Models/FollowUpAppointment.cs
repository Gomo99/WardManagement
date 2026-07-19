using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class FollowUpAppointment
    {
        public int Id { get; set; }

        // Link back to the admission that generated this follow‑up
        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        // Patient is redundant but convenient for direct access
        public int PatientId { get; set; }
        [ForeignKey(nameof(PatientId))]
        public Patient Patient { get; set; } = null!;

        [Required]
        public DateTime AppointmentDate { get; set; }

        [Required, StringLength(100)]
        public string Location { get; set; } = "Outpatient Clinic";

        [StringLength(200)]
        public string? Notes { get; set; }

        // The doctor who scheduled the appointment
        public int CreatedByDoctorId { get; set; }
        [ForeignKey(nameof(CreatedByDoctorId))]
        public Employee CreatedByDoctor { get; set; } = null!;

        // Optionally assign a different doctor for the follow‑up
        public int? AssignedDoctorId { get; set; }
        [ForeignKey(nameof(AssignedDoctorId))]
        public Employee? AssignedDoctor { get; set; }

        public bool IsCompleted { get; set; } = false;
    }
}