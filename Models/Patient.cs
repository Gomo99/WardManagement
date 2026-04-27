using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
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
        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";


        // Test requests (will be added later in the Doctor subsystem)
        // public ICollection<TestRequest> TestRequests { get; set; }
    }
}
