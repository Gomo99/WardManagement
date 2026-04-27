namespace WARDMANAGEMENTSYSTEM.ViewModel
{
    public class EditProfileViewModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string FirstName { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public string LastName { get; set; }

    }
}