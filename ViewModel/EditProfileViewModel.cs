using System.ComponentModel.DataAnnotations;

namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class EditProfileViewModel
    {
        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }
    }
}