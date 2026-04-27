using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Bed
    {
        public int Id { get; set; }

        [Required, StringLength(20)]
        public string BedNumber { get; set; } = null!;

        public int WardId { get; set; }

        [ForeignKey(nameof(WardId))]
        public Ward Ward { get; set; } = null!;

        public bool IsOccupied { get; set; } = false;

        // Soft delete
        public Status IsActive { get; set; } = Status.Active;


        [NotMapped]
        public string BedNumberWithWard => Ward != null ? $"{Ward.Name} - {BedNumber}" : BedNumber;
    }
}