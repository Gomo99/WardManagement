using System.ComponentModel.DataAnnotations;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class ChangeEmailViewModel
    {
        public string CurrentEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "New email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string NewEmail { get; set; } = string.Empty;
    }

    public class ChangePasswordViewModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*\d)(?=.*[^\w\s]).{8,}$",
            ErrorMessage = "Password must be at least 8 characters and contain at least one uppercase letter, one number, and one special character.")]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }


    public class DischargeSummaryViewModel
    {
        public string PatientName { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public DateTime? AdmissionDate { get; set; }
        public DateTime? DischargeDate { get; set; }
        public string? WardName { get; set; }
        public string? BedNumber { get; set; }
        public string? DoctorName { get; set; }
        public List<string> Allergies { get; set; } = new();
        public List<string> Conditions { get; set; } = new();
        public List<TreatmentSummary> Treatments { get; set; } = new();
        public List<MedSummary> Medications { get; set; } = new();
        public List<string?> FollowUpInstructions { get; set; } = new();
    }



    public class EditPatientProfileViewModel
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string CellphoneNumber { get; set; }
        public string Email { get; set; }
        public string HomeAddress { get; set; }
    }




    public class EditProfileViewModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string FirstName { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public string LastName { get; set; }




    }

    public class EmployeeWelcomeEmailViewModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string TempPassword { get; set; }
        public string LoginUrl { get; set; }
    }


    public class FollowUpRequestViewModel
    {
        public int AdmissionId { get; set; }
        public int? PreferredDoctorId { get; set; }    // optional
        public DateTime? PreferredDate { get; set; }
        public string? Reason { get; set; }
    }


    public class ForgotPasswordViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;
    }




    public class LoginViewModel
    {
        [Display(Name = "Username or Email Address")]
        [Required(ErrorMessage = "Username or email is required.")]
        public string UserNameorEmail { get; set; }

        [Display(Name = "Password")]
        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        public string Password { get; set; }

        [Display(Name = "Remember This Device")]
        public bool RememberDevice { get; set; }

        public bool RememberMe { get; set; }
    }


    public class ManageTwoFactorViewModel
    {
        public bool IsTwoFactorEnabled { get; set; }
        public int RecoveryCodesLeft { get; set; }
    }


    public class MedSummary
    {
        public string Name { get; set; } = string.Empty;
        public string? Dosage { get; set; }
        public string? Frequency { get; set; }
        public string? Duration { get; set; }
    }



    public class MyMedicationViewModel
    {
        public string MedicationName { get; set; } = string.Empty;
        public string? Dosage { get; set; }
        public string? Frequency { get; set; }
        public string? Duration { get; set; }
        public DateTime? PrescribedDate { get; set; }
        public DateTime? LastAdministered { get; set; }
    }


    public class PasswordResetEmailViewModel
    {
        public string Email { get; set; }
        public string ResetLink { get; set; }


    }



    public class PatientAppointmentViewModel
    {
        public int VisitId { get; set; }
        public int AdmissionId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public DateTime? VisitDate { get; set; }
        public string? Notes { get; set; }
    }



    public class PatientInstructionViewModel
    {
        public DateTime VisitDate { get; set; }
        public string Instructions { get; set; }
        public string DoctorName { get; set; }

        public int? AdmissionId { get; set; }

    }



    public class PatientLoginCardViewModel
    {
        public string PatientName { get; set; } = string.Empty;
        public int PatientId { get; set; }
        public string PortalUrl { get; set; } = string.Empty;
        public string LoginEmail { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
    }



    public class PatientWelcomeEmailViewModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string TempPassword { get; set; }
        public string LoginUrl { get; set; }
    }



    public class ResetPasswordViewModel
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        [DataType(DataType.Password)]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*\d)(?=.*[^\w\s]).{8,}$",
            ErrorMessage = "Password must be at least 8 characters and contain at least one uppercase letter, one number, and one special character.")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }



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



    public class TreatmentSummary
    {
        public DateTime? Date { get; set; }
        public string? Type { get; set; }
        public string? Notes { get; set; }
    }


    public class TwoFactorChallengeViewModel
    {
        [Display(Name = "Authentication code")]
        public string? Code { get; set; }

        [Display(Name = "Recovery code")]
        public string? RecoveryCode { get; set; }

        public bool UseRecoveryCode { get; set; } = false;

        // Passed through the challenge — needed to complete sign-in
        public string ReturnUrl { get; set; } = string.Empty;

        [Display(Name = "Remember this device (skip 2FA next time)")]
        public bool TrustDevice { get; set; }
    }


    public class TwoFactorRecoveryCodesViewModel
    {
        public List<string> PlainCodes { get; set; } = new();
    }


    public class TwoFactorSetupViewModel
    {
        public string SecretKey { get; set; } = string.Empty;
        public string QrCodeBase64 { get; set; } = string.Empty;  // PNG as base64

        [Required]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Code must be 6 digits.")]
        [Display(Name = "Verification code")]
        public string VerificationCode { get; set; } = string.Empty;
    }



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
