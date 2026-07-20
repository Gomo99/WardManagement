using System;
using System.Collections.Generic;
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
        public OrderStatus OrderStatus { get; set; } = OrderStatus.Ordered;
        public DateTime? ReceivedDate { get; set; }
        public int? QuantityReceived { get; set; }

        // NEW
        public int? MissingQuantity { get; set; }

        [StringLength(200)]
        public string? Notes { get; set; }
        public Status IsActive { get; set; } = Status.Active;
        public DateTime? RejectedAt { get; set; }
        [StringLength(500)]
        public string? RejectionReason { get; set; }
        public int? QuantityFulfilled { get; set; }
        public int? CreatedByEmployeeId { get; set; }
        [ForeignKey(nameof(CreatedByEmployeeId))]
        public Employee? CreatedBy { get; set; }
        public DateTime? EstimatedDeliveryDate { get; set; }
        [StringLength(100)]
        public string? ShippingReference { get; set; }
        [StringLength(100)]
        public string? CourierName { get; set; }
        [StringLength(300)]
        public string? TrackingLink { get; set; }
        public virtual ICollection<ConsumableOrderBatch> ConsumableOrderBatches { get; set; } = new List<ConsumableOrderBatch>();
        public int? SupplierId { get; set; }
        [ForeignKey(nameof(SupplierId))]
        public Employee? Supplier { get; set; }
        public bool IsUrgent { get; set; } = false;
        [StringLength(200)]
        public string? Reason { get; set; }
    }
}