using System.ComponentModel.DataAnnotations;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Notification
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string Message { get; set; } = null!;

        [StringLength(300)]
        public string? Link { get; set; }

        public int? UserId { get; set; }
        public string? UserType { get; set; }

        public string? Role { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsRead { get; set; } = false;
        public Status IsActive { get; set; } = Status.Active;
    }
}