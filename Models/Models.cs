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


        // --- NEW: Social Worker assignment ---
        public int? SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee? SocialWorker { get; set; }

        public int? CreatedByWardAdminId { get; set; }
        [ForeignKey(nameof(CreatedByWardAdminId))]
        public Employee? CreatedByWardAdmin { get; set; }
    }

    public class AdmissionMedication
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int MedicationId { get; set; }
        [ForeignKey(nameof(MedicationId))]
        public Medication Medication { get; set; } = null!;
    }

    public class ConsumableOrder
    {
        public int Id { get; set; }

        public int ConsumableId { get; set; }
        [ForeignKey(nameof(ConsumableId))]
        public Consumable Consumable { get; set; } = null!;

        public int QuantityRequested { get; set; }

        public DateTime RequestDate { get; set; } = DateTime.Now;

        public OrderStatus OrderStatus { get; set; } = OrderStatus.Ordered;   // Ordered, Received, Cancelled etc.

        public DateTime? ReceivedDate { get; set; }
        public int? QuantityReceived { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }

        public Status IsActive { get; set; } = Status.Active;
        public DateTime? RejectedAt { get; set; }
        [StringLength(500)]
        public string? RejectionReason { get; set; }
        public int? QuantityFulfilled { get; set; }   // how many units the supplier has shipped so far
        public int? CreatedByEmployeeId { get; set; }
        [ForeignKey(nameof(CreatedByEmployeeId))]
        public Employee? CreatedBy { get; set; }

        public DateTime? EstimatedDeliveryDate { get; set; }


        [StringLength(100)]
        public string? ShippingReference { get; set; }

        [StringLength(100)]
        public string? CourierName { get; set; }

        [StringLength(300)]
        public string? TrackingLink { get; set; }

        // Add this property if it does not already exist
        public virtual ICollection<ConsumableOrderBatch> ConsumableOrderBatches { get; set; } = new List<ConsumableOrderBatch>();

        public int? SupplierId { get; set; }
        [ForeignKey(nameof(SupplierId))]
        public Employee? Supplier { get; set; }

        public bool IsUrgent { get; set; } = false;

    }

    public class Employee
    {

        [Key]
        public int EmployeeID { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        [Required]
        [StringLength(50)]
        public string UserName { get; set; }

        [Required]
        [StringLength(100)]
        [EmailAddress]
        public string Email { get; set; }

        public string? PasswordHash { get; set; }

        [Display(Name = "Gender")]
        public GenderType Gender { get; set; }

        [Required]
        [Display(Name = "Role")]
        public UserRole Role { get; set; }

        [Display(Name = "Date Hired")]
        [DataType(DataType.Date)]
        public DateTime? HireDate { get; set; }

        public Status IsActive { get; set; } = Status.Active;

        public string? EmailVerificationTokenHash { get; set; }
        public DateTime? EmailVerificationTokenExpires { get; set; }

        public bool IsTwoFactorEnabled { get; set; } = false;
        public string? TwoFactorSecretKey { get; set; }
        public string? TwoFactorRecoveryCodes { get; set; }

        public string? ResetPin { get; set; }
        public DateTime? ResetPinExpiration { get; set; }

        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }
        public bool IsLockedOut => LockoutEnd.HasValue && LockoutEnd > DateTime.Now;

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";

        public bool MustChangePassword { get; set; } = false;
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }

        [StringLength(100)]
        public string? CurrentZone { get; set; }

        public DateTime? CurrentZoneUpdatedAt { get; set; }

        [StringLength(256)]
        public string? PendingEmail { get; set; }

        [StringLength(256)]
        public string? EmailChangeToken { get; set; }

        public DateTime? EmailChangeTokenExpiry { get; set; }

        /// <summary>JSON array of the last 5 password hashes</summary>
        public string? PreviousPasswordHashes { get; set; }

    }

    public class FamilyMeetingAttendee
    {
        public int Id { get; set; }

        public int FamilyMeetingId { get; set; }
        [ForeignKey(nameof(FamilyMeetingId))]
        public FamilyMeeting FamilyMeeting { get; set; } = null!;

        public int EmployeeId { get; set; }
        [ForeignKey(nameof(EmployeeId))]
        public Employee Employee { get; set; } = null!;

        public bool HasAccepted { get; set; } = false;   // default: not yet accepted
    }

    public class Patient
    {
        public int Id { get; set; }

        [Required, StringLength(50)]
        public string FirstName { get; set; } = null!;

        [Required, StringLength(50)]
        public string LastName { get; set; } = null!;

        [Required, StringLength(13)]
        public string SouthAfricanIdNumber { get; set; } = null!; // Unique

        [Required]
        public DateTime DateOfBirth { get; set; }

        [Required, StringLength(20)]
        public string CellphoneNumber { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = null!; // Used as username

        [Required]
        public string HomeAddress { get; set; } = null!;

        // Security
        [Required]
        public string PasswordHash { get; set; } = null!;

        public Status IsActive { get; set; } = Status.Active;

        // Password management
        public bool MustChangePassword { get; set; } = false;
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }
        public Status Status { get; set; } = Status.Active;

        // Lockout
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }

        // ---- NEW: Two‑Factor Authentication ----
        public bool IsTwoFactorEnabled { get; set; } = false;
        public string? TwoFactorSecretKey { get; set; }
        public string? TwoFactorRecoveryCodes { get; set; }

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";

        // Navigation (optional, for device tracking – add if you want patient device trust)
        // public ICollection<UserDevice> UserDevices { get; set; } = new List<UserDevice>();

        [StringLength(256)]
        public string? PendingEmail { get; set; }

        [StringLength(256)]
        public string? EmailChangeToken { get; set; }

        public DateTime? EmailChangeTokenExpiry { get; set; }


        /// <summary>JSON array of the last 5 password hashes</summary>
        public string? PreviousPasswordHashes { get; set; }
    }

    public class Ward
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(500)]
        public string? Description { get; set; }

        public Status IsActive { get; set; } = Status.Active;

        // Navigation property
        public ICollection<Bed> Beds { get; set; } = new List<Bed>();
    }


    public class Vitals
    {
        public int Id { get; set; }

        // Link to the admission (patient ward stay)
        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public DateTime DateRecorded { get; set; } = DateTime.Now;

        [StringLength(10)]
        public string? BloodPressure { get; set; }        // e.g. "120/80"

        [Range(30, 45)]
        public decimal? TemperatureCelsius { get; set; }  // Body temperature

        [Range(0, 50)]
        public decimal? BloodSugarMmolL { get; set; }     // Blood sugar level

        public int? HeartRateBpm { get; set; }            // Pulse

        public int? RespiratoryRate { get; set; }         // Breaths per minute

        public int? OxygenSaturation { get; set; }        // SpO2 percentage (0-100)

        [StringLength(500)]
        public string? Notes { get; set; }

        // Soft delete
        public Status IsActive { get; set; } = Status.Active;
    }

    public class UserDevice
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }        // Employee.Id or Patient.Id

        [Required]
        public string UserType { get; set; } = null!; // "Employee" or "Patient"

        [Required]
        public string DeviceId { get; set; } = null!; // Unique identifier stored in cookie

        [Required]
        public string DeviceName { get; set; } = null!; // User-agent or custom name

        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public bool IsTrusted { get; set; } = false; // For 2FA bypass

        [StringLength(45)]
        public string? IpAddress { get; set; }
    }
    public class Treatment
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(100)]
        public string TreatmentType { get; set; } = null!;   // e.g., "IV Drip Change", "Catheter", "Wound Dressing"

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime TreatmentDate { get; set; } = DateTime.Now;

        public Status IsActive { get; set; } = Status.Active;
    }


    public class StockTakeItem
    {
        public int Id { get; set; }

        public int StockTakeId { get; set; }
        [ForeignKey(nameof(StockTakeId))]
        public StockTake StockTake { get; set; } = null!;

        public int ConsumableId { get; set; }
        [ForeignKey(nameof(ConsumableId))]
        public Consumable Consumable { get; set; } = null!;

        public int SystemQuantity { get; set; }   // what the system thought we had
        public int ActualQuantity { get; set; }   // what was actually counted
    }

    public class StockTake
    {
        public int Id { get; set; }

        public DateTime DateTaken { get; set; } = DateTime.Now;

        [StringLength(200)]
        public string? Notes { get; set; }

        public Status IsActive { get; set; } = Status.Active;

        // Details of stock counted for each consumable
        public ICollection<StockTakeItem> StockTakeItems { get; set; } = new List<StockTakeItem>();

        public int? CreatedByEmployeeId { get; set; }
        [ForeignKey(nameof(CreatedByEmployeeId))]
        public Employee? CreatedBy { get; set; }
    }
    public class RiskScreening
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        public ScreeningType Type { get; set; }

        [Range(0, 100)]
        public int Score { get; set; }

        [StringLength(20)]
        public string? RiskLevel { get; set; }    // Calculated automatically: Low, Medium, High

        [StringLength(2000)]
        public string? RecommendedActions { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }

    public class Referral
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(200)]
        public string OrganisationName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ContactPerson { get; set; }

        [StringLength(20)]
        public string? ContactPhone { get; set; }

        [StringLength(200)]
        public string? ContactEmail { get; set; }

        [StringLength(1000)]
        public string? Reason { get; set; }

        public DateTime DateReferral { get; set; } = DateTime.Now;

        public ReferralOutcome Outcome { get; set; } = ReferralOutcome.Pending;

        [StringLength(1000)]
        public string? OutcomeNotes { get; set; }

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        public bool IsActive { get; set; } = true;
    }


    public class PsychosocialAssessment
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        [StringLength(1000)]
        public string? SocialHistory { get; set; }

        [StringLength(1000)]
        public string? SupportNetwork { get; set; }

        [StringLength(1000)]
        public string? FinancialConcerns { get; set; }

        [StringLength(1000)]
        public string? SubstanceUse { get; set; }

        [StringLength(1000)]
        public string? MentalHealthStatus { get; set; }

        [StringLength(2000)]
        public string? AdditionalNotes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; } = true;
    }


    public class Prescription
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int MedicationId { get; set; }
        [ForeignKey(nameof(MedicationId))]
        public Medication Medication { get; set; } = null!;

        [StringLength(100)]
        public string? Dosage { get; set; }         // e.g. "500mg"

        [StringLength(100)]
        public string? Frequency { get; set; }      // e.g. "Twice daily"

        [StringLength(50)]
        public string? Duration { get; set; }       // e.g. "7 days"

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime PrescribedDate { get; set; } = DateTime.Now;

        public Status IsActive { get; set; } = Status.Active;

        public ScriptStatus ScriptStatus { get; set; } = ScriptStatus.New;
        public int? PharmacistId { get; set; }
        [ForeignKey(nameof(PharmacistId))]
        public Employee? Pharmacist { get; set; }

        public int? ScriptManagerId { get; set; }
        [ForeignKey(nameof(ScriptManagerId))]
        public Employee? ScriptManager { get; set; }

        public bool IsStat { get; set; } = false;
    }

    public class PatientNeed
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(200)]
        public string NeedName { get; set; } = string.Empty;

        public bool IsCompleted { get; set; } = false;

        public int? SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee? SocialWorker { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }




    public class PatientMovement
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(20)]
        public string MovementType { get; set; } = null!;   // "CheckOutRequest", "CheckOut", "CheckIn", etc.

        [Required, StringLength(100)]
        public string Location { get; set; } = null!;

        [StringLength(200)]
        public string? Notes { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public DateTime? Timestamp { get; set; }   // null = pending, set when completed

        // Who performed the movement (nullable – only for actual moves)
        public int? PorterId { get; set; }
        [ForeignKey(nameof(PorterId))]
        public Employee? Porter { get; set; }

        public DateTime? RejectedAt { get; set; }          // set when porter rejects
        [StringLength(200)]
        public string? RejectionReason { get; set; }
        public DateTime? ETA { get; set; }   // estimated time of arrival at destination
        public int? RequestedByWardAdminId { get; set; }   // employee ID of the requesting admin
    }

    public class Notification
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Message { get; set; } = null!;

        [StringLength(300)]
        public string? Link { get; set; }

        // Targeted user (optional)
        public int? UserId { get; set; }
        public string? UserType { get; set; }      // "Employee" or "Patient"

        // Role‑based notification (optional)
        public string? Role { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsRead { get; set; } = false;
        public Status IsActive { get; set; } = Status.Active;
    }
    public class MedicationAdministration
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int MedicationId { get; set; }
        [ForeignKey(nameof(MedicationId))]
        public Medication Medication { get; set; } = null!;

        [StringLength(100)]
        public string? Dosage { get; set; }   // e.g., "500mg"

        public DateTime DateAdministered { get; set; } = DateTime.Now;

        [StringLength(200)]
        public string? Notes { get; set; }

        public Status IsActive { get; set; } = Status.Active;
    }


    public class AllergyMedication
    {
        public int Id { get; set; }

        public int MedicationId { get; set; }
        public Medication Medication { get; set; } = null!;

        public int AllergyId { get; set; }
        public Allergy Allergy { get; set; } = null!;
    }


    public class ConditionMedication
    {
        public int Id { get; set; }

        public int MedicationId { get; set; }
        public Medication Medication { get; set; } = null!;

        public int ConditionId { get; set; }
        public Condition Condition { get; set; } = null!;
    }



    public class Medication
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(200)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string? DosageForm { get; set; }  // e.g., Tablet, Injection, Syrup

        public int? Schedule { get; set; }   // 1-8, higher = more controlled
        public Status IsActive { get; set; } = Status.Active;

        public ICollection<AllergyMedication> AllergyMedications { get; set; } = new List<AllergyMedication>();
        public ICollection<ConditionMedication> ConditionMedications { get; set; } = new List<ConditionMedication>();

    }


    public class HospitalLocation
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public Status IsActive { get; set; } = Status.Active;
    }

    public class HospitalInfo
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string HospitalName { get; set; } = null!;

        [StringLength(200)]
        public string? Address { get; set; }

        [StringLength(20)]
        public string? ContactNumber { get; set; }

        [StringLength(100)]
        public string? Email { get; set; }

        public Status IsActive { get; set; } = Status.Active;
    }


    public class FollowUpRequest
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        [ForeignKey(nameof(PatientId))]
        public Patient Patient { get; set; } = null!;

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int? PreferredDoctorId { get; set; }
        [ForeignKey(nameof(PreferredDoctorId))]
        public Employee? PreferredDoctor { get; set; }

        public DateTime? PreferredDate { get; set; }
        public string? Reason { get; set; }
        public DateTime RequestDate { get; set; }
        public FollowUpRequestStatus Status { get; set; }
        public Status IsActive { get; set; }
    }

    public class FollowUp
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public FollowUpType Type { get; set; } = FollowUpType.PhoneCall;

        public DateTime ScheduledDate { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        public FollowUpStatus Status { get; set; } = FollowUpStatus.Pending;

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool IsActive { get; set; } = true;
    }


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



    public class FamilyContactLog
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        [Required, StringLength(100)]
        public string ContactName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Relationship { get; set; }

        public DateTime ContactDate { get; set; } = DateTime.Now;

        [StringLength(2000)]
        public string? Notes { get; set; }

        [StringLength(2000)]
        public string? DecisionsMade { get; set; }

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool IsActive { get; set; } = true;
    }


    public class EmergencyContact
    {
        public int Id { get; set; }

        [Required]
        public int PatientId { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string Relationship { get; set; } = string.Empty;   // e.g. Spouse, Parent, Sibling

        [Required, StringLength(20)]
        public string Phone { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Notes { get; set; }

        // Navigation property (optional)
        public Patient? Patient { get; set; }
    }


    public class DoctorVisit
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        // Who was contacted / visited (nullable if free‑text, selectable from list)
        public int? DoctorId { get; set; }
        [ForeignKey(nameof(DoctorId))]
        public Employee? Doctor { get; set; }

        // If the doctor is not in the system, use this free‑text field
        [StringLength(100)]
        public string? ExternalDoctorName { get; set; }

        [Required]
        public DateTime VisitDate { get; set; } = DateTime.Now;

        [StringLength(1000)]
        public string? Instructions { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        // True = recorded by nurse after a phone call / advice request
        public bool IsContactRecord { get; set; } = false;

        public Status IsActive { get; set; } = Status.Active;
    }

    public class DischargePlanTask
    {
        public int Id { get; set; }

        public int DischargePlanId { get; set; }
        [ForeignKey(nameof(DischargePlanId))]
        public DischargePlan DischargePlan { get; set; } = null!;

        [Required, StringLength(200)]
        public string TaskName { get; set; } = string.Empty;

        public DateTime? DueDate { get; set; }

        public bool IsCompleted { get; set; } = false;

        public int? CompletedBySocialWorkerId { get; set; }
        [ForeignKey(nameof(CompletedBySocialWorkerId))]
        public Employee? CompletedBySocialWorker { get; set; }

        public DateTime? CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool IsActive { get; set; } = true;
    }

    public class DischargePlan
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int SocialWorkerId { get; set; }
        [ForeignKey(nameof(SocialWorkerId))]
        public Employee SocialWorker { get; set; } = null!;

        [Required, StringLength(2000)]
        public string PlanDetails { get; set; } = null!;

        public DischargePlanStatus DischargeStatus { get; set; } = DischargePlanStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Status IsActive { get; set; } = Status.Active;
    }

    public class ConsumableOrderBatch
    {
        public int Id { get; set; }

        public int ConsumableOrderId { get; set; }
        [ForeignKey(nameof(ConsumableOrderId))]
        public ConsumableOrder ConsumableOrder { get; set; } = null!;

        [Required, StringLength(100)]
        public string BatchNumber { get; set; } = string.Empty;

        public int? Quantity { get; set; }    // how many units in this batch, optional

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }


    public class Consumable
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(200)]
        public string? Description { get; set; }

        public int QuantityOnHand { get; set; } = 0;

        public int ReorderLevel { get; set; } = 5;

        public Status IsActive { get; set; } = Status.Active;
    }

    public class Condition
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(200)]
        public string? Description { get; set; }

        public Status IsActive { get; set; } = Status.Active;
    }

    public class Bed
    {
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string BedNumber { get; set; } = null!;

        public int WardId { get; set; }

        [ForeignKey(nameof(WardId))]
        public Ward Ward { get; set; } = null!;

        public bool IsOccupied { get; set; } = false;

        // Soft delete
        public Status IsActive { get; set; } = Status.Active;


        [NotMapped]
        public string BedNumberWithWard => Ward != null ? $"{Ward.Name} - {BedNumber}" : BedNumber;
    }

    public class Allergy
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(200)]
        public string? Description { get; set; }

        public Status IsActive { get; set; } = Status.Active;
    }




    public class AdmissionCondition
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int ConditionId { get; set; }
        [ForeignKey(nameof(ConditionId))]
        public Condition Condition { get; set; } = null!;
    }

    public class AdmissionAllergy
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int AllergyId { get; set; }
        [ForeignKey(nameof(AllergyId))]
        public Allergy Allergy { get; set; } = null!;



    }

}
