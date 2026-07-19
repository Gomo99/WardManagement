using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class AdmissionMedication
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int MedicationId { get; set; }
        [ForeignKey(nameof(MedicationId))]
        public Medication Medication { get; set; } = null!;
        
    }
}