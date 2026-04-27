using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Vitals
    {
        public int Id { get; set; }

        // Link to the admission (patient ward stay)
        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public DateTime DateRecorded { get; set; } = DateTime.Now;

        [StringLength(10)]
        public string? BloodPressure { get; set; }        // e.g. "120/80"

        [Range(30, 45)]
        public decimal? TemperatureCelsius { get; set; }  // Body temperature

        [Range(0, 50)]
        public decimal? BloodSugarMmolL { get; set; }     // Blood sugar level

        public int? HeartRateBpm { get; set; }            // Pulse

        public int? RespiratoryRate { get; set; }         // Breaths per minute

        public int? OxygenSaturation { get; set; }        // SpO2 percentage (0-100)

        [StringLength(500)]
        public string? Notes { get; set; }

        // Soft delete
        public Status IsActive { get; set; } = Status.Active;
    }
}