using System.ComponentModel.DataAnnotations;

namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class TwoFactorSetupViewModel
    {
        public string SecretKey { get; set; } = string.Empty;
        public string QrCodeBase64 { get; set; } = string.Empty;

        [Required]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Code must be 6 digits.")]
        [Display(Name = "Verification code")]
        public string VerificationCode { get; set; } = string.Empty;
    }
}