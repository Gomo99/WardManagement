using System.ComponentModel.DataAnnotations;
using WARDMANAGEMENTSYSTEM.AppStatus;

namespace WARDMANAGEMENTSYSTEM.Models
{
    public class Medication
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = null!;

        [StringLength(200)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string? DosageForm { get; set; }

        public int? Schedule { get; set; }
        public Status IsActive { get; set; } = Status.Active;

        public ICollection<AllergyMedication> AllergyMedications { get; set; } = new List<AllergyMedication>();
        public ICollection<ConditionMedication> ConditionMedications { get; set; } = new List<ConditionMedication>();
    }
}