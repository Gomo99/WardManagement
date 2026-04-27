using System.ComponentModel.DataAnnotations;
using WARDMANAGEMENTSYSTEM.AppStatus;   // for Status enum

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Ward
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(500)]
        public string? Description { get; set; }

        public Status IsActive { get; set; } = Status.Active;

        // Navigation property
        public ICollection<Bed> Beds { get; set; } = new List<Bed>();
    }
}