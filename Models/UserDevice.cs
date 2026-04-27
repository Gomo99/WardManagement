using System.ComponentModel.DataAnnotations;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class UserDevice
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }        // Employee.Id or Patient.Id

        [Required]
        public string UserType { get; set; } = null!; // "Employee" or "Patient"

        [Required]
        public string DeviceId { get; set; } = null!; // Unique identifier stored in cookie

        [Required]
        public string DeviceName { get; set; } = null!; // User-agent or custom name

        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public bool IsTrusted { get; set; } = false; // For 2FA bypass

        [StringLength(45)]
        public string? IpAddress { get; set; }
    }
}
