using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

        public int? MaximumQuantity { get; set; }
        public int? ReorderQuantity { get; set; }

        // Dynamically suggested order amount
        [NotMapped]
        public int SuggestedOrderQuantity =>
            ReorderQuantity ?? (MaximumQuantity.HasValue
                ? MaximumQuantity.Value - QuantityOnHand
                : (ReorderLevel + 10));

        // In Consumable.cs, add this line:
        public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();

    }
}