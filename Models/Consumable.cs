using System.ComponentModel.DataAnnotations;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Consumable
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(200)]
        public string? Description { get; set; }

        public int QuantityOnHand { get; set; } = 0;

        public int ReorderLevel { get; set; } = 5;

        public Status IsActive { get; set; } = Status.Active;
    }
}