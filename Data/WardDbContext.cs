using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WARDMANAGEMENTSYSTEM.AppStatus;
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

            // -----------------------------------------------------------------
            // 1. Global converters (enum -> string, bool -> "True"/"False")
            // -----------------------------------------------------------------
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    var clrType = property.ClrType;

                    if (clrType.IsEnum)
                    {
                        var converterType = typeof(EnumToStringConverter<>).MakeGenericType(clrType);
                        var converter = (ValueConverter)Activator.CreateInstance(converterType)!;
                        property.SetValueConverter(converter);
                    }
                    else if (clrType == typeof(bool) || clrType == typeof(bool?))
                    {
                        property.SetValueConverter(new ValueConverter<bool, string>(
                            v => v.ToString(),
                            v => bool.Parse(v)));
                        property.SetMaxLength(5);
                    }
                }
            }

            // -----------------------------------------------------------------
            // 2. SEED DATA (all tables)
            // -----------------------------------------------------------------

            // -- Employees (one per role, plain‑text passwords) --
            modelBuilder.Entity<Employee>().HasData(
                new Employee
                {
                    EmployeeID = 1,
                    FirstName = "Admin",
                    LastName = "User",
                    UserName = "admin",
                    Email = "admin@hospital.co.za",
                    PasswordHash = "admin123",
                    Gender = GenderType.Male,
                    Role = UserRole.ADMINISTRATOR,
                    HireDate = new DateTime(2023, 1, 10),
                    IsActive = Status.Active,
                    MustChangePassword = false,
                    IsTwoFactorEnabled = false
                },
                new Employee
                {
                    EmployeeID = 2,
                    FirstName = "John",
                    LastName = "Doe",
                    UserName = "wardadmin",
                    Email = "wardadmin@hospital.co.za",
                    PasswordHash = "wardadmin123",
                    Gender = GenderType.Male,
                    Role = UserRole.WARDADMIN,
                    HireDate = new DateTime(2023, 2, 15),
                    IsActive = Status.Active,
                    MustChangePassword = false,
                    IsTwoFactorEnabled = false
                },
                new Employee
                {
                    EmployeeID = 3,
                    FirstName = "Sarah",
                    LastName = "Smith",
                    UserName = "doctor",
                    Email = "doctor@hospital.co.za",
                    PasswordHash = "doctor123",
                    Gender = GenderType.Female,
                    Role = UserRole.DOCTOR,
                    HireDate = new DateTime(2023, 3, 20),
                    IsActive = Status.Active,
                    MustChangePassword = false,
                    IsTwoFactorEnabled = false
                },
                new Employee
                {
                    EmployeeID = 4,
                    FirstName = "Michael",
                    LastName = "Brown",
                    UserName = "nurse",
                    Email = "nurse@hospital.co.za",
                    PasswordHash = "nurse123",
                    Gender = GenderType.Male,
                    Role = UserRole.NURSE,
                    HireDate = new DateTime(2023, 4, 5),
                    IsActive = Status.Active,
                    MustChangePassword = false,
                    IsTwoFactorEnabled = false
                },
                new Employee
                {
                    EmployeeID = 5,
                    FirstName = "Emily",
                    LastName = "Davis",
                    UserName = "sister",
                    Email = "sister@hospital.co.za",
                    PasswordHash = "sister123",
                    Gender = GenderType.Female,
                    Role = UserRole.NURSINGSISTER,
                    HireDate = new DateTime(2023, 4, 12),
                    IsActive = Status.Active,
                    MustChangePassword = false,
                    IsTwoFactorEnabled = false
                },
                new Employee
                {
                    EmployeeID = 6,
                    FirstName = "David",
                    LastName = "Wilson",
                    UserName = "script",
                    Email = "script@hospital.co.za",
                    PasswordHash = "script123",
                    Gender = GenderType.Male,
                    Role = UserRole.SCRIPTMANAGER,
                    HireDate = new DateTime(2023, 5, 18),
                    IsActive = Status.Active,
                    MustChangePassword = false,
                    IsTwoFactorEnabled = false
                },
                new Employee
                {
                    EmployeeID = 7,
                    FirstName = "Laura",
                    LastName = "Taylor",
                    UserName = "consum",
                    Email = "consum@hospital.co.za",
                    PasswordHash = "consum123",
                    Gender = GenderType.Female,
                    Role = UserRole.CONSUMABLESMANAGER,
                    HireDate = new DateTime(2023, 6, 22),
                    IsActive = Status.Active,
                    MustChangePassword = false,
                    IsTwoFactorEnabled = false
                }
            );

            // -- Patients --
            modelBuilder.Entity<Patient>().HasData(
                new Patient
                {
                    Id = 1,
                    FirstName = "Alice",
                    LastName = "Johnson",
                    SouthAfricanIdNumber = "8001015009087",
                    DateOfBirth = new DateTime(1980, 1, 1),
                    CellphoneNumber = "0721234567",
                    Email = "patient@example.com",
                    HomeAddress = "123 Main Street, Cape Town",
                    PasswordHash = "patient123",
                    IsActive = Status.Active,
                    MustChangePassword = false,
                    IsTwoFactorEnabled = false
                }
            );

            // -- Wards --
            modelBuilder.Entity<Ward>().HasData(
                new Ward { Id = 1, Name = "General Ward A", Description = "General admissions", IsActive = Status.Active },
                new Ward { Id = 2, Name = "Surgical Ward", Description = "Post-operative care", IsActive = Status.Active }
            );

            // -- Beds --
            modelBuilder.Entity<Bed>().HasData(
                new Bed { Id = 1, BedNumber = "A-01", WardId = 1, IsOccupied = false, IsActive = Status.Active },
                new Bed { Id = 2, BedNumber = "A-02", WardId = 1, IsOccupied = false, IsActive = Status.Active },
                new Bed { Id = 3, BedNumber = "S-01", WardId = 2, IsOccupied = false, IsActive = Status.Active },
                new Bed { Id = 4, BedNumber = "S-02", WardId = 2, IsOccupied = false, IsActive = Status.Active }
            );

            // -- Allergies --
            modelBuilder.Entity<Allergy>().HasData(
                new Allergy { Id = 1, Name = "Penicillin", Description = "Allergic to penicillin antibiotics" },
                new Allergy { Id = 2, Name = "Latex", Description = "Latex allergy" }
            );

            // -- Conditions --
            modelBuilder.Entity<Condition>().HasData(
                new Condition { Id = 1, Name = "Hypertension", Description = "High blood pressure" },
                new Condition { Id = 2, Name = "Diabetes Type 2", Description = "Non-insulin dependent diabetes" }
            );

            // -- Medications --
            modelBuilder.Entity<Medication>().HasData(
                new Medication { Id = 1, Name = "Paracetamol", DosageForm = "Tablet", Schedule = 1 },
                new Medication { Id = 2, Name = "Ibuprofen", DosageForm = "Tablet", Schedule = 2 },
                new Medication { Id = 3, Name = "Morphine", DosageForm = "Injection", Schedule = 5 },
                new Medication { Id = 4, Name = "Cough Syrup", DosageForm = "Syrup", Schedule = null }
            );

            // -- Consumables --
            modelBuilder.Entity<Consumable>().HasData(
                new Consumable { Id = 1, Name = "Sterile Gloves", QuantityOnHand = 100, ReorderLevel = 20 },
                new Consumable { Id = 2, Name = "Linen Savers", QuantityOnHand = 50, ReorderLevel = 10 },
                new Consumable { Id = 3, Name = "Syringe 5ml", QuantityOnHand = 200, ReorderLevel = 50 }
            );

            // -- HospitalInfo --
            modelBuilder.Entity<HospitalInfo>().HasData(
                new HospitalInfo
                {
                    Id = 1,
                    HospitalName = "Local Community Hospital",
                    Address = "456 Health Street, Cape Town",
                    ContactNumber = "021 123 4567",
                    Email = "info@localhospital.co.za",
                    IsActive = Status.Active
                }
            );
        }
    }
}