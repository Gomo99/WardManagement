using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    public class WardAdminController : Controller
    {
        private readonly WardDbContext _context;

        public WardAdminController(WardDbContext context)
        {
            _context = context;
        }

        // ---------------------------------------------------------------
        //  DASHBOARD
        // ---------------------------------------------------------------
        public IActionResult Dashboard()
        {
            return View();
        }

        // ===============================================================
        //  PATIENT ADMISSION – STEP 1 (Personal Details)
        // ===============================================================

        [HttpGet]
        public IActionResult AdmitPatient()
        {
            return View(new Models.Patient());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdmitPatient(Models.Patient patient)
        {
            if (!string.IsNullOrWhiteSpace(patient.SouthAfricanIdNumber))
            {
                var existing = await _context.Patients
                    .FirstOrDefaultAsync(p => p.SouthAfricanIdNumber == patient.SouthAfricanIdNumber);
                if (existing != null)
                {
                    TempData["AdmitPatientId"] = existing.Id;
                    return RedirectToAction("AdmitStep2");
                }
            }

            ModelState.Remove("Id");
            ModelState.Remove("PasswordHash");
            ModelState.Remove("MustChangePassword");
            ModelState.Remove("ResetToken");
            ModelState.Remove("ResetTokenExpiry");
            ModelState.Remove("Status");
            ModelState.Remove("FailedLoginAttempts");
            ModelState.Remove("LockoutEnd");

            if (!ModelState.IsValid)
                return View(patient);

            patient.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Temp1234!");
            patient.IsActive = Status.Active;
            patient.MustChangePassword = true;

            _context.Patients.Add(patient);
            await _context.SaveChangesAsync();

            TempData["AdmitPatientId"] = patient.Id;
            return RedirectToAction("AdmitStep2");
        }

        // ===============================================================
        //  PATIENT ADMISSION – STEP 2 (Medical details, doctor, bed)
        // ===============================================================

        [HttpGet]
        public async Task<IActionResult> AdmitStep2()
        {
            var patientId = TempData["AdmitPatientId"] as int?;
            if (patientId == null) return RedirectToAction("AdmitPatient");
            TempData.Keep("AdmitPatientId");

            var patient = await _context.Patients.FindAsync(patientId.Value);
            if (patient == null) return RedirectToAction("AdmitPatient");

            ViewData["PatientName"] = $"{patient.FirstName} {patient.LastName}";
            ViewData["PatientId"] = patient.Id;

            ViewBag.Allergies = new MultiSelectList(await _context.Allergies
                .Where(a => a.IsActive == Status.Active).OrderBy(a => a.Name).ToListAsync(), "Id", "Name");
            ViewBag.Medications = new MultiSelectList(await _context.Medications
                .Where(m => m.IsActive == Status.Active).OrderBy(m => m.Name).ToListAsync(), "Id", "Name");
            ViewBag.Conditions = new MultiSelectList(await _context.Conditions
                .Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
            ViewBag.Doctors = new SelectList(await _context.Employees
                .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active).OrderBy(e => e.LastName).ToListAsync(), "EmployeeID", "FullName");
            ViewBag.Beds = new SelectList(await _context.Beds
                .Where(b => b.IsActive == Status.Active && !b.IsOccupied).Include(b => b.Ward).OrderBy(b => b.Ward.Name).ThenBy(b => b.BedNumber).ToListAsync(), "Id", "BedNumberWithWard");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdmitStep2(int patientId, int doctorId, int bedId,
            int[]? allergyIds, int[]? medicationIds, int[]? conditionIds)
        {
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null) return RedirectToAction("AdmitPatient");

            var bed = await _context.Beds.FindAsync(bedId);
            if (bed == null || bed.IsOccupied || bed.IsActive != Status.Active)
            {
                ModelState.AddModelError("", "Selected bed is not available.");
                return RedirectToAction("AdmitStep2");
            }

            var doctor = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeID == doctorId && e.Role == UserRole.DOCTOR && e.IsActive == Status.Active);
            if (doctor == null)
            {
                ModelState.AddModelError("", "Invalid doctor.");
                return RedirectToAction("AdmitStep2");
            }

            var admission = new Admission
            {
                PatientId = patientId,
                BedId = bedId,
                DoctorId = doctorId,
                AdmissionDate = DateTime.Now,
                IsActive = Status.Active,
                CurrentLocation = null   // patient is in ward
            };

            if (allergyIds != null)
                admission.AdmissionAllergies = allergyIds.Select(id => new AdmissionAllergy { AllergyId = id }).ToList();
            if (medicationIds != null)
                admission.AdmissionMedications = medicationIds.Select(id => new AdmissionMedication { MedicationId = id }).ToList();
            if (conditionIds != null)
                admission.AdmissionConditions = conditionIds.Select(id => new AdmissionCondition { ConditionId = id }).ToList();

            bed.IsOccupied = true;

            // Record initial movement (admission)
            admission.PatientMovements = new List<PatientMovement>
            {
                new PatientMovement { MovementType = "Admission", Location = bed.BedNumberWithWard, Timestamp = DateTime.Now }
            };

            _context.Admissions.Add(admission);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Patient admitted successfully.";
            return RedirectToAction("Patients");
        }

        // ===============================================================
        //  PATIENT LIST (current admissions)
        // ===============================================================
        public async Task<IActionResult> Patients()
        {
            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Where(a => a.IsActive == Status.Active)
                .OrderByDescending(a => a.AdmissionDate)
                .ToListAsync();

            return View(admissions);
        }

        // ===============================================================
        //  DISCHARGE PATIENT
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Discharge(int admissionId)
        {
            var admission = await _context.Admissions
                .Include(a => a.Bed)
                .Include(a => a.PatientMovements)
                .FirstOrDefaultAsync(a => a.Id == admissionId);

            if (admission == null) return NotFound();

            admission.IsActive = Status.Inactive;
            admission.DischargeDate = DateTime.Now;
            admission.CurrentLocation = null;
            admission.Bed.IsOccupied = false;

            // Record discharge movement
            admission.PatientMovements.Add(new PatientMovement
            {
                MovementType = "Discharge",
                Location = "Home / Discharged",
                Timestamp = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Patient discharged successfully.";
            return RedirectToAction("Patients");
        }

        // ===============================================================
        //  PATIENT MOVEMENT – CHECK‑OUT (leave ward)
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckOut(int admissionId, string location, string? notes)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                TempData["ErrorMessage"] = "Location is required.";
                return RedirectToAction("Details", new { id = admissionId });
            }

            var admission = await _context.Admissions
                .Include(a => a.PatientMovements)
                .FirstOrDefaultAsync(a => a.Id == admissionId);

            if (admission == null || admission.IsActive != Status.Active)
                return NotFound();

            if (!string.IsNullOrEmpty(admission.CurrentLocation))
            {
                TempData["ErrorMessage"] = "Patient is already out of ward.";
                return RedirectToAction("Details", new { id = admissionId });
            }

            // Check out
            admission.CurrentLocation = location;
            admission.PatientMovements.Add(new PatientMovement
            {
                MovementType = "CheckOut",
                Location = location,
                Notes = notes,
                Timestamp = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Patient checked out to {location}.";
            return RedirectToAction("Details", new { id = admissionId });
        }

        // ===============================================================
        //  PATIENT MOVEMENT – CHECK‑IN (return to ward)
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(int admissionId)
        {
            var admission = await _context.Admissions
                .Include(a => a.PatientMovements)
                .FirstOrDefaultAsync(a => a.Id == admissionId);

            if (admission == null || admission.IsActive != Status.Active)
                return NotFound();

            if (string.IsNullOrEmpty(admission.CurrentLocation))
            {
                TempData["ErrorMessage"] = "Patient is already in the ward.";
                return RedirectToAction("Details", new { id = admissionId });
            }

            var returnedFrom = admission.CurrentLocation;
            admission.CurrentLocation = null;

            admission.PatientMovements.Add(new PatientMovement
            {
                MovementType = "CheckIn",
                Location = admission.Bed?.BedNumberWithWard ?? "Ward",
                Notes = $"Returned from {returnedFrom}",
                Timestamp = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Patient checked in to ward.";
            return RedirectToAction("Details", new { id = admissionId });
        }

        // ===============================================================
        //  ADMISSION DETAILS (with movement history)
        // ===============================================================
        public async Task<IActionResult> Details(int id)
        {
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .Include(a => a.PatientMovements.OrderByDescending(m => m.Timestamp))
                .FirstOrDefaultAsync(a => a.Id == id);

            if (admission == null) return NotFound();
            return View(admission);
        }
    }
}