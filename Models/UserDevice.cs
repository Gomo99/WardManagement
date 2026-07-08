using System.ComponentModel.DataAnnotations;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class UserDevice
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public string UserType { get; set; } = null!;

        [Required]
        public string DeviceId { get; set; } = null!;

        [Required]
        public string DeviceName { get; set; } = null!;

        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public bool IsTrusted { get; set; } = false;

        [StringLength(45)]
        public string? IpAddress { get; set; }
    }
}