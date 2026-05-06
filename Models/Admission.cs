using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Admission
    {
        public int Id { get; set; }

        public int PatientId { get; set; }
        [ForeignKey(nameof(PatientId))]
        public Patient Patient { get; set; } = null!;

        public int BedId { get; set; }
        [ForeignKey(nameof(BedId))]
        public Bed Bed { get; set; } = null!;

        public int DoctorId { get; set; }
        [ForeignKey(nameof(DoctorId))]
        public Employee Doctor { get; set; } = null!;

        // --- NEW: Nurse assignment ---
        public int? NurseId { get; set; }
        [ForeignKey(nameof(NurseId))]
        public Employee? Nurse { get; set; }

        public DateTime AdmissionDate { get; set; } = DateTime.Now;
        public DateTime? DischargeDate { get; set; }
        public Status IsActive { get; set; } = Status.Active;

        public ICollection<AdmissionAllergy> AdmissionAllergies { get; set; } = new List<AdmissionAllergy>();
        public ICollection<AdmissionMedication> AdmissionMedications { get; set; } = new List<AdmissionMedication>();
        public ICollection<AdmissionCondition> AdmissionConditions { get; set; } = new List<AdmissionCondition>();

        [StringLength(100)]
        public string? CurrentLocation { get; set; }

        public ICollection<PatientMovement> PatientMovements { get; set; } = new List<PatientMovement>();


        public int? CreatedByWardAdminId { get; set; }
        [ForeignKey(nameof(CreatedByWardAdminId))]
        public Employee? CreatedByWardAdmin { get; set; }
    }
}