using System.ComponentModel.DataAnnotations;

namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class ChangeEmailViewModel
    {
        public string CurrentEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "New email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string NewEmail { get; set; } = string.Empty;
    }
}