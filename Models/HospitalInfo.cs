using System.ComponentModel.DataAnnotations;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
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
}