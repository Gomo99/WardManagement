using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Components;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;
using WARDMANAGEMENTSYSTEM.ViewModel;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "PATIENT")]
        [Route("[controller]")]


    public class PatientController : Controller
    {
        private readonly WardDbContext _context;
        private readonly INotificationService _notifService;
        public PatientController(WardDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notifService = notificationService;
        }

        // ------------------------------------------------------------------
        //  HELPER – get current logged‑in user ID and role
        // ------------------------------------------------------------------
        private int? GetCurrentUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            return id;
        }

        private string? GetCurrentUserRole()
        {
            return User.FindFirstValue(ClaimTypes.Role);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE DASHBOARD
        // ==================================================================
        public async Task<IActionResult> Dashboard()
        {
            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();

            if (role != UserRole.PATIENT.ToString() || userId == null)
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients.FindAsync(userId.Value);
            if (patient == null || patient.IsActive != Status.Active)
                return RedirectToAction("Login", "Account");

            // Show summary: number of admissions, etc.
            var admissions = await _context.Admissions
                .Where(a => a.PatientId == userId.Value)
                .OrderByDescending(a => a.AdmissionDate)
                .ToListAsync();

            ViewBag.ActiveAdmission = admissions.FirstOrDefault(a => a.IsActive == Status.Active);
            ViewBag.TotalAdmissions = admissions.Count;

            return View(patient);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – VIEW PROFILE
        // ==================================================================

        [HttpGet("MyProfile")]
        public async Task<IActionResult> MyProfile()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients.FindAsync(userId.Value);
            if (patient == null) return NotFound();
            return View(patient);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – EDIT PROFILE
        // ==================================================================
        [HttpGet("EditMyProfile")]
        public async Task<IActionResult> EditMyProfile()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients.FindAsync(userId.Value);
            if (patient == null) return NotFound();

            return View(new EditPatientProfileViewModel
            {
                Id = patient.Id,
                FirstName = patient.FirstName,
                LastName = patient.LastName,
                CellphoneNumber = patient.CellphoneNumber,
                Email = patient.Email,
                HomeAddress = patient.HomeAddress
            });
        }

        [HttpPost("EditMyProfile")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMyProfile(EditPatientProfileViewModel model)
        {
            var userId = GetCurrentUserId();
            if (userId == null || userId != model.Id || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
                return View(model);

            var patient = await _context.Patients.FindAsync(model.Id);
            if (patient == null) return NotFound();

            patient.FirstName = model.FirstName;
            patient.LastName = model.LastName;
            patient.CellphoneNumber = model.CellphoneNumber;
            patient.Email = model.Email;
            patient.HomeAddress = model.HomeAddress;
            // Date of birth and ID number should not be changed by patient

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Profile updated.";
            return RedirectToAction(nameof(MyProfile));
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – MY ADMISSIONS
        // ==================================================================

        [HttpGet("MyAdmissions")]
        public async Task<IActionResult> MyAdmissions()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Where(a => a.PatientId == userId.Value)
                .OrderByDescending(a => a.AdmissionDate)
                .ToListAsync();

            return View(admissions);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – VIEW FOLDER (similar to doctor folder)
        // ==================================================================

        [HttpGet("MyPatientFolder/{int:id}")]
        public async Task<IActionResult> MyPatientFolder(int admissionId)
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.PatientId == userId.Value);

            if (admission == null) return NotFound();

            ViewBag.Vitals = await _context.Vitals
                .Where(v => v.AdmissionId == admissionId && v.IsActive == Status.Active)
                .OrderByDescending(v => v.DateRecorded).ToListAsync();
            ViewBag.Treatments = await _context.Treatments
                .Where(t => t.AdmissionId == admissionId && t.IsActive == Status.Active)
                .OrderByDescending(t => t.TreatmentDate).ToListAsync();
            ViewBag.MedicationAdministrations = await _context.MedicationAdministrations
                .Include(ma => ma.Medication)
                .Where(ma => ma.AdmissionId == admissionId && ma.IsActive == Status.Active)
                .OrderByDescending(ma => ma.DateAdministered).ToListAsync();
            ViewBag.DoctorVisits = await _context.DoctorVisits
                .Include(dv => dv.Doctor)
                .Where(dv => dv.AdmissionId == admissionId && dv.IsActive == Status.Active)
                .OrderByDescending(dv => dv.VisitDate).ToListAsync();

            return View(admission);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – DEACTIVATE ACCOUNT (soft delete)
        // ==================================================================
        [HttpPost("DeactivateMyAccount")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateMyAccount()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients.FindAsync(userId.Value);
            if (patient == null) return NotFound();

            patient.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            // Log out
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

            TempData["SuccessMessage"] = "Your account has been deactivated.";
            return RedirectToAction("Login", "Account");
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – VIEW ALL DOCTOR INSTRUCTIONS
        // ==================================================================

        [HttpGet("MyInstructions")]
        public async Task<IActionResult> MyInstructions()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            // Get all admissions for this patient
            var admissions = await _context.Admissions
                .Where(a => a.PatientId == userId.Value)
                .Select(a => a.Id)
                .ToListAsync();

            // Collect all doctor visit instructions across all admissions
            var instructions = await _context.DoctorVisits
                .Include(v => v.Admission)
                    .ThenInclude(a => a.Patient)
                .Include(v => v.Doctor)
                .Where(v => admissions.Contains(v.AdmissionId)
                            && v.IsActive == Status.Active
                            && !string.IsNullOrEmpty(v.Instructions))
                .OrderByDescending(v => v.VisitDate)
                .Select(v => new PatientInstructionViewModel
                {
                    VisitDate = v.VisitDate,
                    Instructions = v.Instructions,
                    DoctorName = v.Doctor != null ? v.Doctor.FullName : (v.ExternalDoctorName ?? "Doctor"),
                    AdmissionId = v.AdmissionId
                })
                .ToListAsync();

            ViewBag.PatientName = (await _context.Patients.FindAsync(userId.Value))?.FullName;
            return View(instructions);
        }

        [HttpGet("MyMedications")]
        public async Task<IActionResult> MyMedications()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            // Find the active admission for this patient
            var activeAdmission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.PatientId == userId.Value && a.IsActive == Status.Active);

            if (activeAdmission == null)
            {
                TempData["ErrorMessage"] = "You do not have an active admission.";
                return RedirectToAction("Dashboard");
            }

            // Active prescriptions for this admission
            var prescriptions = await _context.Prescriptions
                .Include(p => p.Medication)
                .Where(p => p.AdmissionId == activeAdmission.Id && p.IsActive == Status.Active)
                .ToListAsync();

            // Last administration time for each medication in this admission
            var medicationIds = prescriptions.Select(p => p.MedicationId).Distinct().ToList();
            var lastAdministeredDict = await _context.MedicationAdministrations
                .Where(ma => ma.AdmissionId == activeAdmission.Id &&
                             medicationIds.Contains(ma.MedicationId) &&
                             ma.IsActive == Status.Active)
                .GroupBy(ma => ma.MedicationId)
                .Select(g => new { MedicationId = g.Key, LastGiven = g.Max(ma => ma.DateAdministered) })
                .ToDictionaryAsync(x => x.MedicationId, x => x.LastGiven);

            // Build the view model
            var model = prescriptions.Select(p => new MyMedicationViewModel
            {
                MedicationName = p.Medication?.Name ?? "Unknown",
                Dosage = p.Dosage,
                Frequency = p.Frequency,
                Duration = p.Duration,
                PrescribedDate = p.PrescribedDate,
                LastAdministered = lastAdministeredDict.ContainsKey(p.MedicationId)
                                    ? lastAdministeredDict[p.MedicationId]
                                    : null
            }).ToList();

            ViewBag.PatientName = activeAdmission.Patient?.FullName;
            return View(model);
        }


        [HttpGet("DischargeSummary/{int:id}")]
        public async Task<IActionResult> DischargeSummary(int admissionId)
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            // Load admission – must belong to patient and be discharged (Inactive)
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .FirstOrDefaultAsync(a => a.Id == admissionId
                                        && a.PatientId == userId.Value
                                        && a.IsActive == Status.Inactive);

            if (admission == null)
            {
                TempData["ErrorMessage"] = "Discharged admission not found.";
                return RedirectToAction("MyAdmissions");
            }

            // Gather data for the summary
            var treatments = await _context.Treatments
                .Where(t => t.AdmissionId == admissionId && t.IsActive == Status.Active)
                .OrderBy(t => t.TreatmentDate)
                .ToListAsync();

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Medication)
                .Where(p => p.AdmissionId == admissionId && p.IsActive == Status.Active)
                .OrderBy(p => p.PrescribedDate)
                .ToListAsync();

            var doctorInstructions = await _context.DoctorVisits
                .Where(v => v.AdmissionId == admissionId
                            && v.IsActive == Status.Active
                            && !string.IsNullOrEmpty(v.Instructions))
                .OrderByDescending(v => v.VisitDate)
                .Select(v => v.Instructions)
                .ToListAsync();

            // Build view model
            var vm = new DischargeSummaryViewModel
            {
                PatientName = admission.Patient?.FullName ?? "N/A",
                DateOfBirth = admission.Patient?.DateOfBirth,
                AdmissionDate = admission.AdmissionDate,
                DischargeDate = admission.DischargeDate,
                WardName = admission.Bed?.Ward?.Name,
                BedNumber = admission.Bed?.BedNumber,
                DoctorName = admission.Doctor?.FullName,
                Allergies = admission.AdmissionAllergies?
                    .Select(aa => aa.Allergy?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList() ?? new List<string>(),
                Conditions = admission.AdmissionConditions?
                    .Select(ac => ac.Condition?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList() ?? new List<string>(),
                Treatments = treatments
                    .Select(t => new TreatmentSummary
                    {
                        Date = t.TreatmentDate,
                        Type = t.TreatmentType,
                        Notes = t.Notes
                    }).ToList(),
                Medications = prescriptions
                    .Select(p => new MedSummary
                    {
                        Name = p.Medication?.Name ?? "Unknown",
                        Dosage = p.Dosage,
                        Frequency = p.Frequency,
                        Duration = p.Duration
                    }).ToList(),
                FollowUpInstructions = doctorInstructions
            };

            // Render the HTML view to a string
            var html = await this.RenderViewAsync("DischargeSummaryPdf", vm, true);

            // Convert HTML to PDF using DinkToPdf
            var pdf = HtmlToPdfConverter.Convert(html);
            return File(pdf, "application/pdf", $"DischargeSummary_{admissionId}.pdf");
        }


        // ==================================================================
        //  PATIENT SELF‑SERVICE – UPCOMING DOCTOR VISITS (MY APPOINTMENTS)
        // ==================================================================

        [HttpGet("MyAppointments")]
        public async Task<IActionResult> MyAppointments()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            // Get all admissions for this patient (active or discharged)
            var admissionIds = await _context.Admissions
                .Where(a => a.PatientId == userId.Value)
                .Select(a => a.Id)
                .ToListAsync();

            if (admissionIds.Count == 0)
            {
                ViewBag.PatientName = (await _context.Patients.FindAsync(userId.Value))?.FullName;
                return View(new List<PatientAppointmentViewModel>());
            }

            var upcomingVisits = await _context.DoctorVisits
                .Include(v => v.Doctor)
                .Where(v => admissionIds.Contains(v.AdmissionId)
                            && v.IsActive == Status.Active
                            && v.VisitDate > DateTime.Now        // future only
                            && v.IsContactRecord == false)       // real scheduled visits
                .OrderBy(v => v.VisitDate)
                .Select(v => new PatientAppointmentViewModel
                {
                    VisitId = v.Id,
                    AdmissionId = v.AdmissionId,
                    DoctorName = v.Doctor != null ? v.Doctor.FullName : (v.ExternalDoctorName ?? "Not assigned"),
                    VisitDate = v.VisitDate,
                    Notes = v.Notes
                })
                .ToListAsync();

            ViewBag.PatientName = (await _context.Patients.FindAsync(userId.Value))?.FullName;
            return View(upcomingVisits);
        }


        // ==================================================================
        //  NEW: REQUEST OUTPATIENT FOLLOW‑UP APPOINTMENT (GET)
        // ==================================================================
        [HttpGet("RequestFollowUp/{int:id}")]
        public async Task<IActionResult> RequestFollowUp(int admissionId)
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            // Admission must belong to patient and be discharged (Inactive)
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId
                                        && a.PatientId == userId.Value
                                        && a.IsActive == Status.Inactive);

            if (admission == null)
            {
                TempData["ErrorMessage"] = "You can only request a follow‑up for a discharged admission.";
                return RedirectToAction("MyAdmissions");
            }

            ViewBag.Doctors = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                    .OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName",
                admission.DoctorId);  // pre‑select the doctor from the admission

            var model = new FollowUpRequestViewModel
            {
                AdmissionId = admissionId,
                PreferredDate = DateTime.Now.AddDays(7),   // default to a week from now
            };

            return View(model);
        }

        // ==================================================================
        //  NEW: REQUEST OUTPATIENT FOLLOW‑UP APPOINTMENT (POST)
        // ==================================================================
        [HttpPost("RequestFollowUp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestFollowUp(FollowUpRequestViewModel model)
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            // Re‑validate admission
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == model.AdmissionId
                                        && a.PatientId == userId.Value
                                        && a.IsActive == Status.Inactive);

            if (admission == null)
            {
                ModelState.AddModelError("", "Invalid or non‑discharged admission.");
                ViewBag.Doctors = new SelectList(
                    await _context.Employees
                        .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                        .OrderBy(e => e.LastName).ToListAsync(),
                    "EmployeeID", "FullName", model.PreferredDoctorId);
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Doctors = new SelectList(
                    await _context.Employees
                        .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                        .OrderBy(e => e.LastName).ToListAsync(),
                    "EmployeeID", "FullName", model.PreferredDoctorId);
                return View(model);
            }

            // Save the request
            var request = new FollowUpRequest
            {
                PatientId = userId.Value,
                AdmissionId = model.AdmissionId,
                PreferredDoctorId = model.PreferredDoctorId,
                PreferredDate = model.PreferredDate,
                Reason = model.Reason,
                RequestDate = DateTime.Now,
                IsActive = Status.Active,
                Status = FollowUpRequestStatus.Pending
            };

            _context.FollowUpRequests.Add(request);
            await _context.SaveChangesAsync();

            // Notify the Ward Admin (or a specific admin role)
            try
            {
                string patientName = admission.Patient?.FullName ?? "A patient";
                string link = Url.Action("Index", "FollowUpRequests", null);   // hypothetical admin controller
                await _notifService.NotifyRoleAsync(
                    UserRole.WARDADMIN.ToString(),
                    $"Patient {patientName} has requested an outpatient follow‑up appointment.",
                    link);
            }
            catch { /* silent */ }

            TempData["SuccessMessage"] = "Your follow‑up request has been submitted.";
            return RedirectToAction("MyAdmissions");
        }

        // ==================================================================
        //  EMERGENCY CONTACTS – LIST
        // ==================================================================

        [HttpGet("MyEmergencyContacts")]
        public async Task<IActionResult> MyEmergencyContacts()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var contacts = await _context.EmergencyContacts
                .Where(c => c.PatientId == userId.Value)
                .OrderBy(c => c.Name)
                .ToListAsync();

            return View(contacts);
        }

        // ==================================================================
        //  ADD EMERGENCY CONTACT – GET
        // ==================================================================
        [HttpGet("AddEmergencyContact")]
        public IActionResult AddEmergencyContact()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            return View(new EmergencyContact());
        }

        // ==================================================================
        //  ADD EMERGENCY CONTACT – POST
        // ==================================================================
        [HttpPost("AddEmergencyContact")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddEmergencyContact(EmergencyContact contact)
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("PatientId");
            ModelState.Remove("Patient");

            if (!ModelState.IsValid)
                return View(contact);

            contact.PatientId = userId.Value;
            _context.EmergencyContacts.Add(contact);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Emergency contact added.";
            return RedirectToAction(nameof(MyEmergencyContacts));
        }

        // ==================================================================
        //  EDIT EMERGENCY CONTACT – GET
        // ==================================================================
        [HttpGet("EditEmergencyContact/{int:id}")]
        public async Task<IActionResult> EditEmergencyContact(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var contact = await _context.EmergencyContacts
                .FirstOrDefaultAsync(c => c.Id == id && c.PatientId == userId.Value);
            if (contact == null) return NotFound();

            return View(contact);
        }

        // ==================================================================
        //  EDIT EMERGENCY CONTACT – POST
        // ==================================================================
        [HttpPost("EditEmergencyContact/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEmergencyContact(int id, EmergencyContact posted)
        {
            if (id != posted.Id) return BadRequest();

            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var existing = await _context.EmergencyContacts
                .FirstOrDefaultAsync(c => c.Id == id && c.PatientId == userId.Value);
            if (existing == null) return NotFound();

            ModelState.Remove("PatientId");
            ModelState.Remove("Patient");

            if (!ModelState.IsValid)
                return View(posted);

            existing.Name = posted.Name;
            existing.Relationship = posted.Relationship;
            existing.Phone = posted.Phone;
            existing.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Emergency contact updated.";
            return RedirectToAction(nameof(MyEmergencyContacts));
        }

        // ==================================================================
        //  DELETE EMERGENCY CONTACT – POST
        // ==================================================================
        [HttpPost("DeleteEmergencyContact/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEmergencyContact(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var contact = await _context.EmergencyContacts
                .FirstOrDefaultAsync(c => c.Id == id && c.PatientId == userId.Value);
            if (contact == null) return NotFound();

            _context.EmergencyContacts.Remove(contact);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Emergency contact removed.";
            return RedirectToAction(nameof(MyEmergencyContacts));
        }

    }

}
