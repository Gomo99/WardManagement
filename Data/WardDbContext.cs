using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Data
{
    public class WardDbContext : DbContext
    {
        public WardDbContext(DbContextOptions options) : base(options) { }


        public DbSet<Employee> Employees { get; set; }
        public DbSet<UserDevice> UserDevices { get; set; }
        public DbSet<Patient> Patients { get; set; }

        public DbSet<Ward> Wards { get; set; }
        public DbSet<Bed> Beds { get; set; }
        public DbSet<Consumable> Consumables { get; set; }
        public DbSet<Allergy> Allergies { get; set; }
        public DbSet<Condition> Conditions { get; set; }
        public DbSet<HospitalInfo> HospitalInfos { get; set; }
        public DbSet<Medication> Medications { get; set; }
        public DbSet<DoctorVisit> DoctorVisits { get; set; }
        public DbSet<Admission> Admissions { get; set; }
        public DbSet<AdmissionAllergy> AdmissionAllergies { get; set; }
        public DbSet<AdmissionMedication> AdmissionMedications { get; set; }
        public DbSet<AdmissionCondition> AdmissionConditions { get; set; }
        public DbSet<Vitals> Vitals { get; set; }

        public DbSet<ConsumableOrder> ConsumableOrders { get; set; }
        public DbSet<StockTake> StockTakes { get; set; }
        public DbSet<StockTakeItem> StockTakeItems { get; set; }
        public DbSet<Prescription> Prescriptions { get; set; }
        public DbSet<Treatment> Treatments { get; set; }
        public DbSet<MedicationAdministration> MedicationAdministrations { get; set; }

        public DbSet<PatientMovement> PatientMovements { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    var clrType = property.ClrType;

                    // Convert all enums to strings
                    if (clrType.IsEnum)
                    {
                        var converterType = typeof(EnumToStringConverter<>).MakeGenericType(clrType);
                        var converter = (ValueConverter)Activator.CreateInstance(converterType)!;
                        property.SetValueConverter(converter);
                    }
                    // Convert all booleans to strings ("True"/"False")
                    else if (clrType == typeof(bool) || clrType == typeof(bool?))
                    {
                        property.SetValueConverter(new ValueConverter<bool, string>(
                            v => v.ToString(),
                            v => bool.Parse(v)));
                        property.SetMaxLength(5); // "False" is longest
                    }
                }
            }


        }













    }     }