using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;
using WARDMANAGEMENTSYSTEM.ViewModel;


namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "WARDADMIN")]
    [Route("[controller]")]

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


        private int? GetCurrentWardAdminId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.WARDADMIN.ToString())
                return null;
            return id;
        }


        // ---------------------------------------------------------------
        //  DASHBOARD
        // ---------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            ViewBag.AdmittedCount = await _context.Admissions.CountAsync(a => a.IsActive == Status.Active && a.CurrentLocation == null);
            ViewBag.OutOfWardCount = await _context.Admissions.CountAsync(a => a.IsActive == Status.Active && a.CurrentLocation != null);
            ViewBag.PendingFollowUps = await _context.FollowUpRequests
        .CountAsync(r => r.IsActive == Status.Active && r.Status == FollowUpRequestStatus.Pending);

            return View();
        }

        // ===============================================================
        //  PATIENT ADMISSION – STEP 1 (Personal Details)
        // ===============================================================

        [HttpGet("AdmitPatient")]
        public IActionResult AdmitPatient()
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            return View(new Models.Patient());
        }

        [HttpPost("AdmitPatient")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdmitPatient(Models.Patient patient)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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
                string loginUrl = Url.Action("Login", "Account", null, Request.Scheme)!;
                await _emailService.SendPatientWelcomeEmailAsync(
                    patient.Email,
                    patient.FirstName,
                    patient.LastName,
                    patient.Email,
                    tempPassword,
                    loginUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Email error: " + ex.Message);
            }

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
        [HttpGet("AdmitStep2/{int:id}")]
        public async Task<IActionResult> AdmitStep2(int? patientId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            // If patientId is passed directly (from pre‑admission queue), use it
            if (patientId.HasValue && patientId.Value > 0)
            {
                TempData["AdmitPatientId"] = patientId.Value;   // ensure TempData is set for POST
                var patientDirect = await _context.Patients.FindAsync(patientId.Value);
                if (patientDirect == null) return RedirectToAction("AdmitPatient");

                ViewData["PatientName"] = $"{patientDirect.FirstName} {patientDirect.LastName}";
                ViewData["PatientId"] = patientDirect.Id;
            }
            else
            {
                // Existing fallback to TempData
                var patientIdFromTemp = TempData["AdmitPatientId"] as int?;
                if (patientIdFromTemp == null) return RedirectToAction("AdmitPatient");
                TempData.Keep("AdmitPatientId");

                var patient = await _context.Patients.FindAsync(patientIdFromTemp.Value);
                if (patient == null) return RedirectToAction("AdmitPatient");

                ViewData["PatientName"] = $"{patient.FirstName} {patient.LastName}";
                ViewData["PatientId"] = patient.Id;
            }

            // ---- Common code: populate dropdowns ----
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


        [HttpPost("AdmitStep2/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdmitStep2(int patientId, int doctorId, int nurseId, int bedId,
            int[]? allergyIds, int[]? medicationIds, int[]? conditionIds)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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

        [HttpGet("Patients")]
        public async Task<IActionResult> Patients()
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Where(a => a.IsActive == Status.Active)
                .OrderByDescending(a => a.AdmissionDate)
                .ToListAsync();
            return View(admissions);
        }

        [HttpPost("Discharge/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Discharge(int admissionId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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

        [HttpPost("CheckOut/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckOut(int admissionId, string location, string? notes)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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

        [HttpPost("CheckIn/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(int admissionId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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

        [HttpGet("Details/{int:id}")]
        public async Task<IActionResult> Details(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .Include(a => a.PatientMovements.OrderByDescending(m => m.Timestamp))
                 .ThenInclude(m => m.Porter)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (admission == null) return NotFound();
            return View(admission);
        }

        // ===============================================================
        //  EDIT ADMISSION (with notification on doctor/nurse change)
        // ===============================================================
        [HttpGet("EditAdmission/{int:id}")]
        public async Task<IActionResult> EditAdmission(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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

        [HttpPost("EditAdmission/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAdmission(int id, int doctorId, int nurseId, int bedId,
            int[]? allergyIds, int[]? medicationIds, int[]? conditionIds)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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

        [HttpPost("DeleteAdmission/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAdmission(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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

        [HttpPost("RestoreAdmission/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreAdmission(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


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
        //  REQUEST PORTER MOVEMENT – GET
        // ===============================================================
        // ===============================================================
        //  REQUEST PORTER MOVEMENT – GET (with location dropdown)
        // ===============================================================
        [HttpGet("RequestMovement/{int:id}")]
        public async Task<IActionResult> RequestMovement(int admissionId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);

            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.CurrentBed = admission.Bed?.BedNumberWithWard;
            ViewBag.CurrentLocation = admission.CurrentLocation ?? "In Ward";

            // List of porters for dropdown
            ViewBag.Porters = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.PORTER && e.IsActive == Status.Active)
                    .OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName");

            // ** NEW: Locations dropdown **
            ViewBag.Locations = new SelectList(
                await _context.HospitalLocations
                    .Where(l => l.IsActive == Status.Active)
                    .OrderBy(l => l.Name).ToListAsync(),
                "Name", "Name");   // both value and text are the location name

            return View(new PatientMovement
            {
                AdmissionId = admissionId,
                MovementType = "CheckOutRequest"
            });
        }
        // ===============================================================
        //  REQUEST PORTER MOVEMENT – POST
        // ===============================================================
        [HttpPost("RequestMovement/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestMovement(int admissionId, int porterId,
                                                   string location, string? notes)
        {

            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            if (string.IsNullOrWhiteSpace(location))
            {
                TempData["ErrorMessage"] = "Destination is required.";
                return RedirectToAction(nameof(RequestMovement), new { admissionId });
            }

            var porter = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeID == porterId && e.Role == UserRole.PORTER && e.IsActive == Status.Active);
            if (porter == null)
            {
                TempData["ErrorMessage"] = "Invalid porter.";
                return RedirectToAction(nameof(RequestMovement), new { admissionId });
            }

            // Get the current Ward Admin's ID from the logged‑in user
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? wardAdminId = null;
            if (!string.IsNullOrEmpty(claim) && int.TryParse(claim, out int id))
                wardAdminId = id;

            var movement = new PatientMovement
            {
                AdmissionId = admissionId,
                MovementType = "CheckOutRequest",
                Location = location,
                Notes = notes,
                PorterId = porterId,
                Timestamp = null,                 // pending until porter confirms
                RequestedByWardAdminId = wardAdminId   // store who made the request
            };

            _context.PatientMovements.Add(movement);
            await _context.SaveChangesAsync();

            // Notify the porter
            try
            {
                string wardAdminName = await GetCurrentWardAdminName();
                await _notifService.NotifyUserAsync(
                    porterId,
                    "Employee",
                    $"{wardAdminName} requests you to move {admission.Patient.FullName} to {location}.",
                    Url.Action("MyMovements", "Porter"));
            }
            catch (Exception ex) { Console.WriteLine("Notify porter error: " + ex.Message); }

            TempData["SuccessMessage"] = $"Movement request sent to {porter.FullName}.";
            return RedirectToAction("Details", new { id = admissionId });
        }

        // ===============================================================
        //  DELETE MOVEMENT RECORD (soft delete)
        // ===============================================================
        [HttpPost("DeleteMovement/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMovement(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var movement = await _context.PatientMovements.FindAsync(id);
            if (movement == null) return NotFound();


            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Movement record deleted.";
            return RedirectToAction("Details", new { id = movement.AdmissionId });
        }


        // ===============================================================
        //  PATIENT RECORDS (personal details, not admissions)
        // ===============================================================
        [HttpGet("PatientRecords")]
        public async Task<IActionResult> PatientRecords(string status = "Active")
        {
            var query = _context.Patients.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsed))
                query = query.Where(p => p.IsActive == parsed);

            var patients = await query.OrderBy(p => p.LastName).ToListAsync();

            ViewBag.Statuses = new SelectList(new List<SelectListItem>
    {
        new SelectListItem("Active", "Active", status == "Active"),
        new SelectListItem("Inactive", "Inactive", status == "Inactive"),
        new SelectListItem("All", "All", status == "All")
    }, "Value", "Text", status);

            return View(patients);
        }



        // EDIT PATIENT PERSONAL DETAILS – GET
        [HttpGet("EditPatient/{int:id}")]
        public async Task<IActionResult> EditPatient(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var patient = await _context.Patients.FindAsync(id);
            if (patient == null) return NotFound();
            return View(patient);
        }

        // EDIT PATIENT PERSONAL DETAILS – POST
        [HttpPost("EditPatient/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPatient(int id, Models.Patient posted)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            if (id != posted.Id) return BadRequest();

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

            if (!ModelState.IsValid) return View(posted);

            var patient = await _context.Patients.FindAsync(id);
            if (patient == null) return NotFound();

            // Check unique ID number if changed
            if (patient.SouthAfricanIdNumber != posted.SouthAfricanIdNumber)
            {
                bool exists = await _context.Patients.AnyAsync(p => p.SouthAfricanIdNumber == posted.SouthAfricanIdNumber && p.Id != id);
                if (exists)
                {
                    ModelState.AddModelError("SouthAfricanIdNumber", "This ID number is already in use.");
                    return View(posted);
                }
            }

            patient.FirstName = posted.FirstName;
            patient.LastName = posted.LastName;
            patient.SouthAfricanIdNumber = posted.SouthAfricanIdNumber;
            patient.DateOfBirth = posted.DateOfBirth;
            patient.CellphoneNumber = posted.CellphoneNumber;
            patient.Email = posted.Email;
            patient.HomeAddress = posted.HomeAddress;
            patient.IsActive = Status.Active;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Patient details updated.";
            return RedirectToAction(nameof(PatientRecords));
        }



        // SOFT DELETE PATIENT
        [HttpPost("DeletePatient/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePatient(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var patient = await _context.Patients.FindAsync(id);
            if (patient == null) return NotFound();

            patient.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Patient deactivated.";
            return RedirectToAction(nameof(PatientRecords));
        }

        // RESTORE PATIENT
        [HttpPost("RestorePatient/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestorePatient(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var patient = await _context.Patients.FindAsync(id);
            if (patient == null) return NotFound();

            patient.IsActive = Status.Active;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Patient reactivated.";
            return RedirectToAction(nameof(PatientRecords));
        }






        // ===============================================================
        //  RESEND PATIENT PASSWORD (generate new temporary password & email)
        // ===============================================================
        [HttpPost("ResendPatientPassword/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendPatientPassword(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var patient = await _context.Patients.FindAsync(id);
            if (patient == null) return NotFound();

            // Generate new temporary password
            string tempPassword = GenerateRandomPassword(12);
            patient.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            patient.MustChangePassword = true;
            await _context.SaveChangesAsync();

            // Email the new password
            try
            {
                string loginUrl = Url.Action("Login", "Account", null, Request.Scheme)!;
                await _emailService.SendPatientWelcomeEmailAsync(
                    patient.Email,
                    patient.FirstName,
                    patient.LastName,
                    patient.Email,
                    tempPassword,
                    loginUrl);

                TempData["SuccessMessage"] = $"New temporary password sent to {patient.Email}.";
            }
            catch (Exception ex)
            {
                TempData["SuccessMessage"] = $"Password updated, but email delivery failed ({ex.Message}).";
            }

            return RedirectToAction(nameof(PatientRecords));
        }




        // ===============================================================
        //  PRINT PATIENT WRISTBAND
        // ===============================================================
        [HttpPost("PrintWristband/{int:id}")]
        public async Task<IActionResult> PrintWristband(int admissionId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            // Load admission with patient, bed, ward, doctor
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == admissionId);

            if (admission == null) return NotFound();

            // Generate a barcode from the admission ID (or patient ID)
            var barcodeContent = admissionId.ToString(); // You can use PatientId or a custom format
            var barcodeBase64 = GenerateBarcodeBase64(barcodeContent);

            var model = new WristbandViewModel
            {
                PatientName = admission.Patient?.FullName ?? "N/A",
                PatientId = admission.PatientId.ToString(),
                AdmissionId = admission.Id,
                BedNumber = admission.Bed?.BedNumberWithWard ?? "N/A",
                WardName = admission.Bed?.Ward?.Name ?? "N/A",
                DoctorName = admission.Doctor?.FullName ?? "N/A",
                DateOfBirth = admission.Patient != null
    ? admission.Patient.DateOfBirth.ToString("dd MMM yyyy")
    : "N/A",
                BarcodeBase64 = barcodeBase64
            };

            return View(model);
        }

        // Helper to generate barcode image as Base64
        private string GenerateBarcodeBase64(string content)
        {
            // Use ZXing.Net to create a barcode
            var writer = new ZXing.SkiaSharp.BarcodeWriter
            {
                Format = ZXing.BarcodeFormat.CODE_128,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = 300,
                    Height = 80,
                    Margin = 1
                }
            };

            using var bitmap = writer.Write(content);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return Convert.ToBase64String(data.ToArray());
        }


        // ===============================================================
        //  PRINT PATIENT LOGIN INSTRUCTION CARD
        // ===============================================================

        [HttpPost("PatientLoginCard/{int:id}")]
        public async Task<IActionResult> PatientLoginCard(int patientId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null) return NotFound();

            var model = new PatientLoginCardViewModel
            {
                PatientName = patient.FullName,
                PatientId = patient.Id,
                PortalUrl = Url.Action("Login", "Account", null, Request.Scheme)!,
                LoginEmail = patient.Email,
                Instructions = "1. Open the portal link above.\n2. Log in with your email address.\n3. If you don't know your password, click 'Forgot Password' on the login page."
            };

            return View(model);
        }


        // ===============================================================
        //  VIEW PENDING PORTER REQUESTS
        // ===============================================================

        [HttpGet("PendingPorterRequests")]
        public async Task<IActionResult> PendingPorterRequests()
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var requests = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .Include(m => m.Porter)
                .Where(m => m.MovementType == "CheckOutRequest" && m.Timestamp == null) // still pending
                .OrderByDescending(m => m.Id)
                .ToListAsync();

            return View(requests);
        }

        // ===============================================================
        //  CANCEL PORTER REQUEST
        // ===============================================================
        [HttpPost("CancelMovementRequest/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelMovementRequest(int id)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var movement = await _context.PatientMovements
                .Include(m => m.Admission)
                .FirstOrDefaultAsync(m => m.Id == id && m.MovementType == "CheckOutRequest" && m.Timestamp == null);

            if (movement == null) return NotFound();

            // Mark the request as cancelled
            movement.RejectedAt = DateTime.Now;
            movement.RejectionReason = "Cancelled by Ward Admin";
            await _context.SaveChangesAsync();

            // ----- Notify the porter -----
            if (movement.PorterId.HasValue)
            {
                try
                {
                    string adminName = await GetCurrentWardAdminName();
                    string patientName = movement.Admission?.Patient?.FullName ?? "a patient";
                    string msg = $"{adminName} has cancelled the movement request for {patientName}.";

                    await _notifService.NotifyUserAsync(
                        movement.PorterId.Value,
                        "Employee",
                        msg,
                        Url.Action("MyMovements", "Porter"));
                }
                catch (Exception ex) { Console.WriteLine("Notify porter error: " + ex.Message); }
            }

            TempData["SuccessMessage"] = "Porter request cancelled and porter notified.";
            return RedirectToAction(nameof(PendingPorterRequests));
        }



        // ===============================================================
        //  REASSIGN PORTER – GET (choose new porter)
        // ===============================================================
        [HttpGet("ReassignPorter/{int:id}")]
        public async Task<IActionResult> ReassignPorter(int movementId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var movement = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .Include(m => m.Porter)
                .FirstOrDefaultAsync(m => m.Id == movementId &&
                                         m.MovementType == "CheckOutRequest" &&
                                         m.Timestamp == null);

            if (movement == null) return NotFound();

            // Get all active porters except the currently assigned one
            ViewBag.Porters = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.PORTER && e.IsActive == Status.Active && e.EmployeeID != movement.PorterId)
                    .OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName");

            ViewBag.PatientName = movement.Admission?.Patient?.FullName;
            ViewBag.Destination = movement.Location;
            return View(movement);
        }

        // ===============================================================
        //  REASSIGN PORTER – POST
        // ===============================================================
        [HttpPost("ReassignPorter/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReassignPorter(int movementId, int newPorterId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var movement = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .Include(m => m.Porter)
                .FirstOrDefaultAsync(m => m.Id == movementId &&
                                         m.MovementType == "CheckOutRequest" &&
                                         m.Timestamp == null);

            if (movement == null) return NotFound();

            var newPorter = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeID == newPorterId && e.Role == UserRole.PORTER && e.IsActive == Status.Active);
            if (newPorter == null)
            {
                TempData["ErrorMessage"] = "Selected porter is invalid.";
                return RedirectToAction(nameof(ReassignPorter), new { movementId });
            }

            int? oldPorterId = movement.PorterId;

            // Reassign and reset acceptance/rejection flags
            movement.PorterId = newPorterId;
            movement.AcceptedAt = null;
            movement.RejectedAt = null;
            movement.RejectionReason = null;

            await _context.SaveChangesAsync();

            // Notify the new porter
            try
            {
                string adminName = await GetCurrentWardAdminName();
                string patientName = movement.Admission?.Patient?.FullName ?? "a patient";
                string msg = $"{adminName} has assigned you a movement request for {patientName} to {movement.Location}.";
                await _notifService.NotifyUserAsync(
                    newPorterId,
                    "Employee",
                    msg,
                    Url.Action("MyMovements", "Porter"));
            }
            catch (Exception ex) { Console.WriteLine("Notify new porter error: " + ex.Message); }

            // Optionally notify the old porter
            if (oldPorterId.HasValue && oldPorterId != newPorterId)
            {
                try
                {
                    string adminName = await GetCurrentWardAdminName();
                    string patientName = movement.Admission?.Patient?.FullName ?? "a patient";
                    string msg = $"{adminName} has reassigned the movement request for {patientName} to another porter.";
                    await _notifService.NotifyUserAsync(
                        oldPorterId.Value,
                        "Employee",
                        msg,
                        null);
                }
                catch (Exception ex) { Console.WriteLine("Notify old porter error: " + ex.Message); }
            }

            TempData["SuccessMessage"] = $"Movement reassigned to {newPorter.FullName}.";
            return RedirectToAction(nameof(PendingPorterRequests));
        }

        // ===============================================================
        //  FOLLOW‑UP REQUESTS – LIST
        // ===============================================================

        [HttpGet("FollowUpRequests")]
        public async Task<IActionResult> FollowUpRequests(string status = "All")
        {

            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var query = _context.FollowUpRequests
                .Include(f => f.Patient)
                .Include(f => f.Admission)
                .Include(f => f.PreferredDoctor)
                .Where(f => f.IsActive == Status.Active);

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<FollowUpRequestStatus>(status, out var parsedStatus))
            {
                query = query.Where(f => f.Status == parsedStatus);
            }

            var requests = await query
                .OrderByDescending(f => f.RequestDate)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            return View(requests);
        }

        // ===============================================================
        //  SCHEDULE FOLLOW‑UP REQUEST (confirm)
        // ===============================================================
        [HttpPost("ScheduleFollowUp/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScheduleFollowUp(int id)
        {

            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var request = await _context.FollowUpRequests
                .Include(f => f.PatientId)
                .FirstOrDefaultAsync(f => f.Id == id && f.IsActive == Status.Active && f.Status == FollowUpRequestStatus.Pending);

            if (request == null) return NotFound();

            request.Status = FollowUpRequestStatus.Scheduled;
            await _context.SaveChangesAsync();

            // Notify the patient
            try
            {
                string patientName = request.Patient?.FullName ?? "Patient";
                string msg = $"Your follow‑up appointment has been scheduled. Preferred date: {request.PreferredDate:dd MMM yyyy}.";
        await _notifService.NotifyUserAsync(
            request.PatientId,
            "Patient",
            msg,
            Url.Action("MyAdmissions", "Patient"));
            }
            catch (Exception ex) { Console.WriteLine("Notify patient error: " + ex.Message); }

            TempData["SuccessMessage"] = "Follow‑up request scheduled.";
            return RedirectToAction(nameof(FollowUpRequests));
        }

        // ===============================================================
        //  CANCEL FOLLOW‑UP REQUEST
        // ===============================================================
        [HttpPost("CancelFollowUp/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelFollowUp(int id)
        {

            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var request = await _context.FollowUpRequests
                .Include(f => f.PatientId)
                .FirstOrDefaultAsync(f => f.Id == id && f.IsActive == Status.Active && f.Status == FollowUpRequestStatus.Pending);

            if (request == null) return NotFound();

            request.Status = FollowUpRequestStatus.Cancelled;
            await _context.SaveChangesAsync();

            // Notify the patient (optional)
            try
            {
                string patientName = request.Patient?.FullName ?? "Patient";   
                string msg = $"Your follow‑up appointment request has been cancelled. Please contact the hospital for further information.";
                await _notifService.NotifyUserAsync(
                    request.PatientId,
                    "Patient",
                    msg,
                    null);
            }
            catch (Exception ex) { Console.WriteLine("Notify patient error: " + ex.Message); }

            TempData["SuccessMessage"] = "Follow‑up request cancelled.";
            return RedirectToAction(nameof(FollowUpRequests));
        }


        // ===============================================================
        //  VIEW SOCIAL WORKER DISCHARGE PLANS (READ‑ONLY)
        // ===============================================================
        [HttpGet("DischargePlans/{int:id}")]
        public async Task<IActionResult> DischargePlans(int admissionId)
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);

            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient?.FullName;
            ViewBag.AdmissionId = admissionId;

            // Load all active discharge plans for this admission, with the social worker who created them
            var plans = await _context.DischargePlans
                .Include(p => p.SocialWorker)
                .Where(p => p.AdmissionId == admissionId && p.IsActive == Status.Active)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            // Optionally load tasks for each plan (to show task count/completion)
            // We'll use a dictionary to avoid multiple queries
            var planIds = plans.Select(p => p.Id).ToList();
            var tasksDict = await _context.DischargePlanTasks
                .Where(t => planIds.Contains(t.DischargePlanId) && t.IsActive)
                .GroupBy(t => t.DischargePlanId)
                .Select(g => new { PlanId = g.Key, Count = g.Count(), Completed = g.Count(t => t.IsCompleted) })
                .ToDictionaryAsync(x => x.PlanId, x => (x.Count, x.Completed));

            ViewBag.TasksDict = tasksDict;

            return View(plans);
        }


        // ===============================================================
        //  PRE‑ADMISSION QUEUE
        // ===============================================================
        [HttpGet("PreAdmissionQueue")]
        public async Task<IActionResult> PreAdmissionQueue()
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            // Patients who are active, but have no active admission
            var patientsWithoutAdmission = await _context.Patients
                .Where(p => p.IsActive == Status.Active &&
                            !_context.Admissions.Any(a => a.PatientId == p.Id && a.IsActive == Status.Active))
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .ToListAsync();

            return View(patientsWithoutAdmission);
        }

        [HttpGet("PorterLocations")]
        public async Task<IActionResult> PorterLocations()
        {
            int? supplierId = GetCurrentWardAdminId();
            if (supplierId == null) return RedirectToAction("Login", "Account");


            var porters = await _context.Employees
                .Where(e => e.Role == UserRole.PORTER && e.IsActive == Status.Active)
                .OrderBy(e => e.CurrentZone)
                .ThenBy(e => e.LastName)
                .ToListAsync();

            return View(porters);
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