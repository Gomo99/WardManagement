using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class ConsumableOrder
    {
        public int Id { get; set; }

        public int ConsumableId { get; set; }
        [ForeignKey(nameof(ConsumableId))]
        public Consumable Consumable { get; set; } = null!;

        public int QuantityRequested { get; set; }

        public DateTime RequestDate { get; set; } = DateTime.Now;

        public OrderStatus OrderStatus { get; set; } = OrderStatus.Ordered;   // Ordered, Received, Cancelled etc.

        public DateTime? ReceivedDate { get; set; }
        public int? QuantityReceived { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }

        public Status IsActive { get; set; } = Status.Active;
    }
}