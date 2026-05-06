using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    public class WardAdminController : Controller
    {
        private readonly WardDbContext _context;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notifService;   // <-- notification service

        public WardAdminController(WardDbContext context,
                                   IEmailService emailService,
                                   INotificationService notifService)
        {
            _context = context;
            _emailService = emailService;
            _notifService = notifService;
        }

        // ---------------------------------------------------------------
        //  DASHBOARD
        // ---------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            ViewBag.AdmittedCount = await _context.Admissions.CountAsync(a => a.IsActive == Status.Active && a.CurrentLocation == null);
            ViewBag.OutOfWardCount = await _context.Admissions.CountAsync(a => a.IsActive == Status.Active && a.CurrentLocation != null);
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
            // --- Existing Patient Lookup ---
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

            // --- Remove fields not submitted / set manually ---
            ModelState.Remove("Id");
            ModelState.Remove("PasswordHash");
            ModelState.Remove("MustChangePassword");
            ModelState.Remove("ResetToken");
            ModelState.Remove("ResetTokenExpiry");
            ModelState.Remove("Status");
            ModelState.Remove("FailedLoginAttempts");
            ModelState.Remove("LockoutEnd");
            ModelState.Remove("IsTwoFactorEnabled");
            ModelState.Remove("TwoFactorSecretKey");
            ModelState.Remove("TwoFactorRecoveryCodes");

            if (!ModelState.IsValid)
                return View(patient);

            // 1. Generate a random temporary password
            string tempPassword = GenerateRandomPassword(12);
            patient.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            patient.IsActive = Status.Active;
            patient.MustChangePassword = true;

            _context.Patients.Add(patient);
            await _context.SaveChangesAsync();   // Patient now has an Id

            // 2. Email the temporary password
            try
            {
                string subject = "Welcome to Our Hospital – Your Patient Account";
                string body = $@"
                    <h3>Hello {patient.FirstName} {patient.LastName},</h3>
                    <p>An account has been created for you at our hospital.</p>
                    <p><strong>Email:</strong> {patient.Email}</p>
                    <p><strong>Temporary Password:</strong> {tempPassword}</p>
                    <p>You will be required to change your password after your first login.</p>
                    <p>Visit the login page: <a href='{Url.Action("Login", "Account", null, Request.Scheme)}'>Login</a></p>";
                await _emailService.SendEmailAsync(patient.Email, subject, body);
            }
            catch (Exception ex) { Console.WriteLine("Email error: " + ex.Message); }

            // 3. In-app notification to the new patient
            try
            {
                string adminName = await GetCurrentWardAdminName();
                string notificationMsg = $"Your patient account was created by {adminName}. Please log in to update your password.";
                await _notifService.NotifyUserAsync(
                    patient.Id,
                    "Patient",
                    notificationMsg,
                    Url.Action("Login", "Account", null, Request.Scheme));
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["AdmitPatientId"] = patient.Id;
            TempData["SuccessMessage"] = $"Patient account created – temporary password emailed and notification sent.";
            return RedirectToAction("AdmitStep2");
        }

        // ===============================================================
        //  PATIENT ADMISSION – STEP 2 (Medical details, doctor, nurse, bed)
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

            // Doctors
            ViewBag.Doctors = new SelectList(await _context.Employees
                .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                .OrderBy(e => e.LastName).ToListAsync(), "EmployeeID", "FullName");

            // Nurses
            ViewBag.Nurses = new SelectList(await _context.Employees
                .Where(e => e.Role == UserRole.NURSE && e.IsActive == Status.Active)
                .OrderBy(e => e.LastName).ToListAsync(), "EmployeeID", "FullName");

            ViewBag.Beds = new SelectList(await _context.Beds
                .Where(b => b.IsActive == Status.Active && !b.IsOccupied)
                .Include(b => b.Ward).OrderBy(b => b.Ward.Name).ThenBy(b => b.BedNumber).ToListAsync(), "Id", "BedNumberWithWard");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdmitStep2(int patientId, int doctorId, int nurseId, int bedId,
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

            var nurse = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeID == nurseId && e.Role == UserRole.NURSE && e.IsActive == Status.Active);
            if (nurse == null)
            {
                ModelState.AddModelError("", "Invalid nurse.");
                return RedirectToAction("AdmitStep2");
            }

            var admission = new Admission
            {
                PatientId = patientId,
                BedId = bedId,
                DoctorId = doctorId,
                NurseId = nurseId,
                AdmissionDate = DateTime.Now,
                IsActive = Status.Active,
                CurrentLocation = null
            };

            if (allergyIds != null)
                admission.AdmissionAllergies = allergyIds.Select(id => new AdmissionAllergy { AllergyId = id }).ToList();
            if (medicationIds != null)
                admission.AdmissionMedications = medicationIds.Select(id => new AdmissionMedication { MedicationId = id }).ToList();
            if (conditionIds != null)
                admission.AdmissionConditions = conditionIds.Select(id => new AdmissionCondition { ConditionId = id }).ToList();

            bed.IsOccupied = true;
            admission.PatientMovements = new List<PatientMovement>
            {
                new PatientMovement { MovementType = "Admission", Location = bed.BedNumberWithWard, Timestamp = DateTime.Now }
            };

            // Capture the ward admin who created the admission
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(claim) && int.TryParse(claim, out int wardAdminId))
            {
                admission.CreatedByWardAdminId = wardAdminId;
            }


            _context.Admissions.Add(admission);
            await _context.SaveChangesAsync();

            // --------------- NOTIFICATIONS ---------------
            try
            {
                string adminName = await GetCurrentWardAdminName();
                string patientFullName = $"{patient.FirstName} {patient.LastName}";

                // Notify doctor
                await _notifService.NotifyUserAsync(
                    doctorId,
                    "Employee",
                    $"{adminName} has assigned you a new patient: {patientFullName}.",
                    Url.Action("PatientFolder", "Doctor", new { admissionId = admission.Id }));

                // Notify nurse
                await _notifService.NotifyUserAsync(
                    nurseId,
                    "Employee",
                    $"{adminName} has assigned you a new patient: {patientFullName}.",
                    Url.Action("Patients", "Nurse"));

                TempData["SuccessMessage"] = "Patient admitted successfully – doctor and nurse notified.";
            }
            catch (Exception ex)
            {
                TempData["SuccessMessage"] = "Patient admitted (notifications failed).";
                Console.WriteLine("Notification error: " + ex.Message);
            }

            return RedirectToAction("Patients");
        }

        // ===============================================================
        //  PATIENT LIST, DISCHARGE, MOVEMENT, DETAILS – unchanged
        //  (I'm including them for completeness)
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
            if (admission == null || admission.IsActive != Status.Active) return NotFound();
            if (!string.IsNullOrEmpty(admission.CurrentLocation))
            {
                TempData["ErrorMessage"] = "Patient is already out of ward.";
                return RedirectToAction("Details", new { id = admissionId });
            }
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(int admissionId)
        {
            var admission = await _context.Admissions
                .Include(a => a.PatientMovements)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null || admission.IsActive != Status.Active) return NotFound();
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

        // ===============================================================
        //  EDIT ADMISSION (with notification on doctor/nurse change)
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> EditAdmission(int id)
        {
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.Nurse)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .FirstOrDefaultAsync(a => a.Id == id && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;

            ViewBag.SelectedAllergyIds = admission.AdmissionAllergies.Select(a => a.AllergyId).ToList();
            ViewBag.SelectedMedicationIds = admission.AdmissionMedications.Select(m => m.MedicationId).ToList();
            ViewBag.SelectedConditionIds = admission.AdmissionConditions.Select(c => c.ConditionId).ToList();

            ViewBag.Allergies = new MultiSelectList(await _context.Allergies
                .Where(a => a.IsActive == Status.Active).OrderBy(a => a.Name).ToListAsync(), "Id", "Name", ViewBag.SelectedAllergyIds);
            ViewBag.Medications = new MultiSelectList(await _context.Medications
                .Where(m => m.IsActive == Status.Active).OrderBy(m => m.Name).ToListAsync(), "Id", "Name", ViewBag.SelectedMedicationIds);
            ViewBag.Conditions = new MultiSelectList(await _context.Conditions
                .Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name).ToListAsync(), "Id", "Name", ViewBag.SelectedConditionIds);

            ViewBag.Doctors = new SelectList(await _context.Employees
                .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active).OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName", admission.DoctorId);

            ViewBag.Nurses = new SelectList(await _context.Employees
                .Where(e => e.Role == UserRole.NURSE && e.IsActive == Status.Active).OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName", admission.NurseId);

            ViewBag.Beds = new SelectList(await _context.Beds
                .Where(b => b.IsActive == Status.Active && (b.Id == admission.BedId || !b.IsOccupied))
                .Include(b => b.Ward).OrderBy(b => b.Ward.Name).ThenBy(b => b.BedNumber).ToListAsync(),
                "Id", "BedNumberWithWard", admission.BedId);

            return View(admission);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAdmission(int id, int doctorId, int nurseId, int bedId,
            int[]? allergyIds, int[]? medicationIds, int[]? conditionIds)
        {
            var admission = await _context.Admissions
                .Include(a => a.AdmissionAllergies)
                .Include(a => a.AdmissionMedications)
                .Include(a => a.AdmissionConditions)
                .Include(a => a.Bed)
                .FirstOrDefaultAsync(a => a.Id == id && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            // Store old assignees for notification comparison
            int oldDoctorId = admission.DoctorId;
            int? oldNurseId = admission.NurseId;

            var doctor = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeID == doctorId && e.Role == UserRole.DOCTOR && e.IsActive == Status.Active);
            if (doctor == null)
            {
                TempData["ErrorMessage"] = "Invalid doctor.";
                return RedirectToAction("EditAdmission", new { id });
            }

            var nurse = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeID == nurseId && e.Role == UserRole.NURSE && e.IsActive == Status.Active);
            if (nurse == null)
            {
                TempData["ErrorMessage"] = "Invalid nurse.";
                return RedirectToAction("EditAdmission", new { id });
            }

            if (admission.BedId != bedId)
            {
                var newBed = await _context.Beds.FindAsync(bedId);
                if (newBed == null || (newBed.IsOccupied && newBed.Id != admission.BedId) || newBed.IsActive != Status.Active)
                {
                    TempData["ErrorMessage"] = "Selected bed is not available.";
                    return RedirectToAction("EditAdmission", new { id });
                }
                admission.Bed.IsOccupied = false;
                newBed.IsOccupied = true;
                admission.BedId = bedId;
            }

            admission.DoctorId = doctorId;
            admission.NurseId = nurseId;

            _context.AdmissionAllergies.RemoveRange(admission.AdmissionAllergies);
            _context.AdmissionMedications.RemoveRange(admission.AdmissionMedications);
            _context.AdmissionConditions.RemoveRange(admission.AdmissionConditions);
            if (allergyIds != null)
                _context.AdmissionAllergies.AddRange(allergyIds.Select(a => new AdmissionAllergy { AdmissionId = id, AllergyId = a }));
            if (medicationIds != null)
                _context.AdmissionMedications.AddRange(medicationIds.Select(m => new AdmissionMedication { AdmissionId = id, MedicationId = m }));
            if (conditionIds != null)
                _context.AdmissionConditions.AddRange(conditionIds.Select(c => new AdmissionCondition { AdmissionId = id, ConditionId = c }));

            await _context.SaveChangesAsync();

            // --------------- NOTIFICATIONS FOR ASSIGNEE CHANGES ---------------
            try
            {
                string adminName = await GetCurrentWardAdminName();
                string patientFullName = admission.Patient.FullName;

                // Notify new doctor if changed
                if (oldDoctorId != doctorId)
                {
                    await _notifService.NotifyUserAsync(
                        doctorId, "Employee",
                        $"{adminName} has assigned you to patient {patientFullName}.",
                        Url.Action("PatientFolder", "Doctor", new { admissionId = id }));
                }

                // Notify new nurse if changed
                if (oldNurseId != nurseId)
                {
                    await _notifService.NotifyUserAsync(
                        nurseId, "Employee",
                        $"{adminName} has assigned you to patient {patientFullName}.",
                        Url.Action("Patients", "Nurse"));
                }

                
                if (oldDoctorId != doctorId && oldDoctorId != 0)
                {
                    await _notifService.NotifyUserAsync(oldDoctorId, "Employee",
                        $"You are no longer assigned to patient {patientFullName}.", null);
                }
                if (oldNurseId.HasValue && oldNurseId.Value != nurseId)
                {
                    await _notifService.NotifyUserAsync(oldNurseId.Value, "Employee",
                        $"You are no longer assigned to patient {patientFullName}.", null);
                }
                

                TempData["SuccessMessage"] = "Admission updated – staff notified.";
            }
            catch (Exception ex)
            {
                TempData["SuccessMessage"] = "Admission updated (notifications failed).";
                Console.WriteLine("Edit notification error: " + ex.Message);
            }

            return RedirectToAction("Details", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAdmission(int id)
        {
            var admission = await _context.Admissions
                .Include(a => a.Bed)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (admission == null) return NotFound();
            if (admission.Bed != null)
                admission.Bed.IsOccupied = false;
            admission.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Patient folder deactivated (soft deleted).";
            return RedirectToAction("Patients");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreAdmission(int id)
        {
            var admission = await _context.Admissions
                .Include(a => a.Bed)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (admission == null) return NotFound();
            if (admission.Bed != null && admission.Bed.IsOccupied)
            {
                TempData["ErrorMessage"] = "Cannot restore – the bed is now occupied by another patient.";
                return RedirectToAction("Details", new { id });
            }
            admission.IsActive = Status.Active;
            if (admission.Bed != null)
                admission.Bed.IsOccupied = true;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Admission restored successfully.";
            return RedirectToAction("Details", new { id });
        }

        // ===============================================================
        //  PRIVATE HELPERS
        // ===============================================================
        private static string GenerateRandomPassword(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%";
            byte[] data = RandomNumberGenerator.GetBytes(length);
            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = chars[data[i] % chars.Length];
            }
            return new string(result);
        }

        private async Task<string> GetCurrentWardAdminName()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(claim) && int.TryParse(claim, out int adminId))
            {
                var admin = await _context.Employees.FindAsync(adminId);
                if (admin != null)
                    return $"{admin.FirstName} {admin.LastName}";
            }
            return "Hospital Staff";
        }
    }
}