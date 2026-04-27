using System.ComponentModel.DataAnnotations.Schema;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class AdmissionAllergy
    {
        public int Id { get; set; }

        public int AdmissionId { get; set; }
        [ForeignKey(nameof(AdmissionId))]
        public Admission Admission { get; set; } = null!;

        public int AllergyId { get; set; }
        [ForeignKey(nameof(AllergyId))]
        public Allergy Allergy { get; set; } = null!;
    }
}