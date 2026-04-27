using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class StockTakeItem
    {
        public int Id { get; set; }

        public int StockTakeId { get; set; }
        [ForeignKey(nameof(StockTakeId))]
        public StockTake StockTake { get; set; } = null!;

        public int ConsumableId { get; set; }
        [ForeignKey(nameof(ConsumableId))]
        public Consumable Consumable { get; set; } = null!;

        public int SystemQuantity { get; set; }   // what the system thought we had
        public int ActualQuantity { get; set; }   // what was actually counted
    }
}