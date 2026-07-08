using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class ConsumableOrderBatch
    {
        public int Id { get; set; }

        public int ConsumableOrderId { get; set; }
        [ForeignKey(nameof(ConsumableOrderId))]
        public ConsumableOrder ConsumableOrder { get; set; } = null!;

        [Required, StringLength(100)]
        public string BatchNumber { get; set; } = string.Empty;

        public int? Quantity { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}