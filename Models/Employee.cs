using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
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

    }
}

