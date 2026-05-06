using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class StockTake
    {
        public int Id { get; set; }

        public DateTime DateTaken { get; set; } = DateTime.Now;

        [StringLength(200)]
        public string? Notes { get; set; }

        public Status IsActive { get; set; } = Status.Active;

        // Details of stock counted for each consumable
        public ICollection<StockTakeItem> StockTakeItems { get; set; } = new List<StockTakeItem>();

        public int? CreatedByEmployeeId { get; set; }
        [ForeignKey(nameof(CreatedByEmployeeId))]
        public Employee? CreatedBy { get; set; }
    }
}