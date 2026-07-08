using System.ComponentModel.DataAnnotations;

namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;
    }
}