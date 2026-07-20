using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class StockMovement
    {
        public int Id { get; set; }

        public int ConsumableId { get; set; }
        [ForeignKey(nameof(ConsumableId))]
        public Consumable Consumable { get; set; } = null!;

        public int QuantityChange { get; set; } // positive for additions, negative for removals

        public MovementType MovementType { get; set; }

        [Required, StringLength(200)]
        public string? Reason { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.Now;

        // Optional link to the related order or stock take
        public int? ConsumableOrderId { get; set; }
        [ForeignKey(nameof(ConsumableOrderId))]
        public ConsumableOrder? ConsumableOrder { get; set; }

        public int? StockTakeId { get; set; }
        [ForeignKey(nameof(StockTakeId))]
        public StockTake? StockTake { get; set; }
    }
}
