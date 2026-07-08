using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Vitals
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public DateTime DateRecorded { get; set; } = DateTime.Now;

        [StringLength(10)]
        public string? BloodPressure { get; set; }

        [Range(30, 45)]
        public decimal? TemperatureCelsius { get; set; }

        [Range(0, 50)]
        public decimal? BloodSugarMmolL { get; set; }

        public int? HeartRateBpm { get; set; }

        public int? RespiratoryRate { get; set; }

        public int? OxygenSaturation { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public Status IsActive { get; set; } = Status.Active;
    }
}