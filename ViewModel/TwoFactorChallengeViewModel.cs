using System.ComponentModel.DataAnnotations;

namespace WARDMANAGEMENTSYSTEM.ViewModel
{
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
}
