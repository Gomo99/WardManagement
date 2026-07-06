using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;
using WARDMANAGEMENTSYSTEM.ViewModel;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "DOCTOR")]
    // [Route("[controller]")]   <-- REMOVED
    public class DoctorController : Controller
    {
        private readonly WardDbContext _context;
        private readonly INotificationService _notifService;

        public DoctorController(WardDbContext context, INotificationService notifService)
        {
            _context = context;
            _notifService = notifService;
        }

        // ------------------------------------------------------------------
        //  HELPER – get current Doctor's EmployeeID from login
        // ------------------------------------------------------------------
        private int? GetCurrentDoctorId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            return id;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            ViewBag.ActivePatients = await _context.Admissions.CountAsync(a => a.DoctorId == doctorId && a.IsActive == Status.Active);
            ViewBag.UpcomingVisits = await _context.DoctorVisits.CountAsync(dv => dv.DoctorId == doctorId && dv.IsActive == Status.Active && dv.VisitDate > DateTime.Now);
            return View();
        }

        // ==================================================================
        //  MY PATIENTS – list of admissions assigned to this doctor
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> MyPatients()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => a.DoctorId == doctorId.Value && a.IsActive == Status.Active)
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();

            return View(admissions);
        }

        // ==================================================================
        //  PATIENT FOLDER – comprehensive overview
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PatientFolder(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value);

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

            ViewBag.PatientMovements = await _context.PatientMovements
                .Where(pm => pm.AdmissionId == admissionId)
                .OrderByDescending(pm => pm.Timestamp).ToListAsync();

            return View(admission);
        }

        // ==================================================================
        //  RECORD DOCTOR VISIT (GET)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> RecordVisit(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value);

            if (admission == null) return NotFound();

            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            ViewBag.AdmissionId = admissionId;

            return View(new DoctorVisit
            {
                AdmissionId = admissionId,
                VisitDate = DateTime.Now,
                DoctorId = doctorId.Value,
                IsContactRecord = false
            });
        }

        // ==================================================================
        //  RECORD DOCTOR VISIT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordVisit(DoctorVisit visit)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            visit.DoctorId = doctorId.Value;
            visit.IsContactRecord = false;

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Doctor");
            ModelState.Remove("ExternalDoctorName");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == visit.AdmissionId && a.DoctorId == doctorId.Value);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = visit.AdmissionId;
                return View(visit);
            }

            var valid = await _context.Admissions
                .AnyAsync(a => a.Id == visit.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (!valid)
            {
                ModelState.AddModelError("", "You are not authorised to record a visit for this patient.");
                return View(visit);
            }

            visit.IsActive = Status.Active;
            _context.DoctorVisits.Add(visit);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit recorded successfully.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        // ==================================================================
        //  EDIT DOCTOR VISIT (GET)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> EditVisit(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);

            if (visit == null) return NotFound();

            ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
            return View(visit);
        }

        // ==================================================================
        //  EDIT DOCTOR VISIT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditVisit(int id, DoctorVisit posted)
        {
            if (id != posted.Id) return BadRequest();
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var existing = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);
            if (existing == null) return NotFound();

            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Doctor");
            ModelState.Remove("ExternalDoctorName");

            if (!ModelState.IsValid)
            {
                var visit = await _context.DoctorVisits
                    .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(dv => dv.Id == id);
                if (visit != null)
                    ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
                return View(posted);
            }

            existing.VisitDate = posted.VisitDate;
            existing.Instructions = posted.Instructions;
            existing.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit updated.";
            return RedirectToAction("PatientFolder", new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  DOCTOR VISIT DETAILS
        // ==================================================================
        [HttpPost]
        public async Task<IActionResult> VisitDetails(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .Include(dv => dv.Doctor)
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value);

            if (visit == null) return NotFound();
            return View(visit);
        }

        // ==================================================================
        //  SOFT DELETE VISIT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteVisit(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value);
            if (visit == null) return NotFound();

            visit.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit deactivated.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        // ==================================================================
        //  RESTORE VISIT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreVisit(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value);
            if (visit == null) return NotFound();

            visit.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit reactivated.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        // ==================================================================
        //  TREATMENTS (doctor can perform treatments during visits)
        // ==================================================================
        [HttpPost]
        public async Task<IActionResult> TreatmentsByAdmission(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = (await _context.Patients.FindAsync(admission.PatientId))?.FullName;

            var treatments = await _context.Treatments
                .Where(t => t.AdmissionId == admissionId && t.IsActive == Status.Active)
                .OrderByDescending(t => t.TreatmentDate)
                .ToListAsync();
            return View(treatments);
        }

        // ==================================================================
        //  RECORD TREATMENT (GET)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> RecordTreatment(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            return View(new Treatment { AdmissionId = admissionId, TreatmentDate = DateTime.Now });
        }

        // ==================================================================
        //  RECORD TREATMENT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordTreatment(Treatment treatment)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == treatment.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = treatment.AdmissionId;
                return View(treatment);
            }

            var valid = await _context.Admissions
                .AnyAsync(a => a.Id == treatment.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (!valid) return BadRequest("You are not authorised to record a treatment for this patient.");

            treatment.IsActive = Status.Active;
            _context.Treatments.Add(treatment);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment recorded.";
            return RedirectToAction("TreatmentsByAdmission", new { admissionId = treatment.AdmissionId });
        }

        // ==================================================================
        //  EDIT TREATMENT (GET)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> EditTreatment(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive == Status.Active && t.Admission.DoctorId == doctorId.Value);
            if (treatment == null) return NotFound();

            ViewBag.PatientName = (await _context.Patients.FindAsync(treatment.Admission.PatientId))?.FullName;
            return View(treatment);
        }

        // ==================================================================
        //  EDIT TREATMENT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTreatment(int id, Treatment posted)
        {
            if (id != posted.Id) return BadRequest();
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var existing = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive == Status.Active && t.Admission.DoctorId == doctorId.Value);
            if (existing == null) return NotFound();

            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");

            if (!ModelState.IsValid)
            {
                ViewBag.PatientName = (await _context.Patients.FindAsync(existing.Admission.PatientId))?.FullName;
                return View(posted);
            }

            existing.TreatmentType = posted.TreatmentType;
            existing.Notes = posted.Notes;
            existing.TreatmentDate = posted.TreatmentDate;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment updated.";
            return RedirectToAction("TreatmentsByAdmission", new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  TREATMENT DETAILS
        // ==================================================================
        [HttpPost]
        public async Task<IActionResult> TreatmentDetails(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.DoctorId == doctorId.Value);
            if (treatment == null) return NotFound();
            return View(treatment);
        }

        // ==================================================================
        //  SOFT DELETE TREATMENT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTreatment(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.DoctorId == doctorId.Value);
            if (treatment == null) return NotFound();

            treatment.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment deactivated.";
            return RedirectToAction("TreatmentsByAdmission", new { admissionId = treatment.AdmissionId });
        }

        // ==================================================================
        //  RESTORE TREATMENT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreTreatment(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.DoctorId == doctorId.Value);
            if (treatment == null) return NotFound();

            treatment.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment reactivated.";
            return RedirectToAction("TreatmentsByAdmission", new { admissionId = treatment.AdmissionId });
        }

        // ==================================================================
        //  SCHEDULE A VISIT (future date, no instructions initially)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ScheduleVisit(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);

            if (admission == null) return NotFound();

            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            ViewBag.AdmissionId = admissionId;

            return View(new DoctorVisit
            {
                AdmissionId = admissionId,
                VisitDate = DateTime.Now.AddHours(1),
                DoctorId = doctorId.Value,
                IsContactRecord = false
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScheduleVisit(DoctorVisit visit)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            visit.DoctorId = doctorId.Value;
            visit.IsContactRecord = false;
            visit.Instructions = null;
            visit.Notes = null;

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Doctor");
            ModelState.Remove("ExternalDoctorName");
            ModelState.Remove("Instructions");
            ModelState.Remove("Notes");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == visit.AdmissionId && a.DoctorId == doctorId.Value);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = visit.AdmissionId;
                return View(visit);
            }

            var valid = await _context.Admissions
                .AnyAsync(a => a.Id == visit.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (!valid)
            {
                ModelState.AddModelError("", "You are not authorised to schedule a visit for this patient.");
                return View(visit);
            }

            visit.IsActive = Status.Active;
            _context.DoctorVisits.Add(visit);
            await _context.SaveChangesAsync();

            // --------------- NOTIFICATION TO PATIENT ---------------
            try
            {
                string doctorName = (await _context.Employees.FindAsync(doctorId))?.FullName ?? "Doctor";
                var admission = await _context.Admissions.Include(a => a.Patient).FirstOrDefaultAsync(a => a.Id == visit.AdmissionId);
                if (admission?.PatientId != null)
                {
                    string patientName = admission.Patient.FullName;
                    string patientLink = Url.Action("MyPatientFolder", "Patient", new { admissionId = visit.AdmissionId });
                    await _notifService.NotifyUserAsync(
                        admission.PatientId,
                        "Patient",
                        $"{doctorName} has scheduled a visit for you on {visit.VisitDate:ddd, dd MMM yyyy HH:mm}.",
                        patientLink);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Visit scheduled.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        // ==================================================================
        //  SCHEDULED VISITS (list upcoming visits for the current doctor)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> MyScheduledVisits()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var upcoming = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .Include(dv => dv.Admission.Bed).ThenInclude(b => b.Ward)
                .Where(dv => dv.DoctorId == doctorId.Value
                             && dv.IsActive == Status.Active
                             && dv.VisitDate > DateTime.Now)
                .OrderBy(dv => dv.VisitDate)
                .ToListAsync();

            return View(upcoming);
        }

        // ==================================================================
        //  WRITE / EDIT INSTRUCTIONS for a scheduled visit (GET)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> WriteInstructions(int visitId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(dv => dv.Id == visitId && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);

            if (visit == null) return NotFound();

            ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
            ViewBag.VisitId = visitId;
            ViewBag.ExistingInstructions = visit.Instructions;

            return View();
        }

        // ==================================================================
        //  WRITE / EDIT INSTRUCTIONS for a scheduled visit (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WriteInstructions(int visitId, string instructions)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission)
                    .ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(dv => dv.Id == visitId && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);
            if (visit == null) return NotFound();

            if (string.IsNullOrWhiteSpace(instructions))
            {
                ModelState.AddModelError("", "Instructions cannot be empty.");
                ViewBag.PatientName = visit.Admission.Patient.FullName;
                ViewBag.VisitId = visitId;
                ViewBag.ExistingInstructions = visit.Instructions;
                return View();
            }

            visit.Instructions = instructions;
            await _context.SaveChangesAsync();

            // --------------- NOTIFICATION TO ASSIGNED NURSE ---------------
            try
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == visit.AdmissionId);

                if (admission?.NurseId != null)
                {
                    string doctorName = (await _context.Employees.FindAsync(doctorId))?.FullName ?? "The doctor";
                    string patientName = admission.Patient.FullName;
                    string nurseLink = Url.Action("DoctorVisitsByAdmission", "Nurse", new { admissionId = visit.AdmissionId });

                    await _notifService.NotifyUserAsync(
                        admission.NurseId.Value,
                        "Employee",
                        $"{doctorName} has written new instructions for patient {patientName}.",
                        nurseLink);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Instructions saved.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        // ==================================================================
        //  VIEW ALL INSTRUCTIONS FOR AN ADMISSION
        // ==================================================================
        [HttpPost]
        public async Task<IActionResult> PatientInstructions(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            var patient = await _context.Patients.FindAsync(admission.PatientId);
            ViewBag.PatientName = patient?.FullName;

            var instructionsList = await _context.DoctorVisits
                .Where(dv => dv.AdmissionId == admissionId
                             && dv.IsActive == Status.Active
                             && !string.IsNullOrEmpty(dv.Instructions))
                .OrderByDescending(dv => dv.VisitDate)
                .Select(dv => new PatientInstructionViewModel
                {
                    VisitDate = dv.VisitDate,
                    Instructions = dv.Instructions,
                    DoctorName = dv.Doctor != null ? dv.Doctor.FullName : "Unknown"
                })
                .ToListAsync();

            return View(instructionsList);
        }

        // ==================================================================
        //  PRESCRIPTIONS – LIST FOR AN ADMISSION
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PrescriptionsByAdmission(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = (await _context.Patients.FindAsync(admission.PatientId))?.FullName;

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Medication)
                .Where(p => p.AdmissionId == admissionId && p.IsActive == Status.Active)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();

            return View(prescriptions);
        }

        // ==================================================================
        //  PRESCRIBE MEDICATION – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PrescribeMedication(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            ViewBag.AdmissionId = admissionId;

            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active)
                    .OrderBy(m => m.Name)
                    .ToListAsync(),
                "Id", "Name");

            // Script Managers dropdown
            ViewBag.ScriptManagers = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.SCRIPTMANAGER && e.IsActive == Status.Active)
                    .OrderBy(e => e.LastName)
                    .ToListAsync(),
                "EmployeeID", "FullName");

            return View(new Prescription
            {
                AdmissionId = admissionId,
                PrescribedDate = DateTime.Now
            });
        }

        // ==================================================================
        //  PRESCRIBE MEDICATION – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrescribeMedication(Prescription prescription)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");
            ModelState.Remove("ScriptManager");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == prescription.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = prescription.AdmissionId;
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active)
                        .OrderBy(m => m.Name).ToListAsync(),
                    "Id", "Name", prescription.MedicationId);
                ViewBag.ScriptManagers = new SelectList(
                    await _context.Employees
                        .Where(e => e.Role == UserRole.SCRIPTMANAGER && e.IsActive == Status.Active)
                        .OrderBy(e => e.LastName).ToListAsync(),
                    "EmployeeID", "FullName", prescription.ScriptManagerId);
                return View(prescription);
            }

            var valid = await _context.Admissions
                .AnyAsync(a => a.Id == prescription.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (!valid) return BadRequest("You are not authorised to prescribe for this patient.");

            prescription.IsActive = Status.Active;
            prescription.ScriptStatus = ScriptStatus.New;
            _context.Prescriptions.Add(prescription);
            await _context.SaveChangesAsync();

            // --------------- NOTIFICATIONS ---------------
            try
            {
                string doctorName = (await _context.Employees.FindAsync(doctorId))?.FullName ?? "Doctor";
                var admission = await _context.Admissions.Include(a => a.Patient).FirstOrDefaultAsync(a => a.Id == prescription.AdmissionId);
                string patientName = admission?.Patient.FullName ?? "a patient";
                string medName = (await _context.Medications.FindAsync(prescription.MedicationId))?.Name ?? "medication";
                string patientLink = Url.Action("MyInstructions", "Patient");

                // 1. Notify Script Manager (if assigned)
                if (prescription.ScriptManagerId.HasValue)
                {
                    string scriptLink = Url.Action("NewScripts", "ScriptManager");
                    await _notifService.NotifyUserAsync(
                        prescription.ScriptManagerId.Value,
                        "Employee",
                        $"{doctorName} assigned you a new prescription for {patientName}: {medName}.",
                        scriptLink);
                }

                // 2. Notify Patient
                int? patientUserId = admission?.PatientId;
                if (patientUserId.HasValue)
                {
                    await _notifService.NotifyUserAsync(
                        patientUserId.Value,
                        "Patient",
                        $"{doctorName} has prescribed {medName} for you.",
                        patientLink);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Medication prescribed.";
            return RedirectToAction("PrescriptionsByAdmission", new { admissionId = prescription.AdmissionId });
        }

        // ==================================================================
        //  EDIT PRESCRIPTION – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> EditPrescription(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value && p.IsActive == Status.Active);
            if (prescription == null) return NotFound();

            ViewBag.PatientName = $"{prescription.Admission.Patient.FirstName} {prescription.Admission.Patient.LastName}";
            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active)
                    .OrderBy(m => m.Name).ToListAsync(),
                "Id", "Name", prescription.MedicationId);

            return View(prescription);
        }

        // ==================================================================
        //  EDIT PRESCRIPTION – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPrescription(int id, Prescription posted)
        {
            if (id != posted.Id) return BadRequest();
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var existing = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value && p.IsActive == Status.Active);
            if (existing == null) return NotFound();

            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");

            if (!ModelState.IsValid)
            {
                var prescription = await _context.Prescriptions
                    .Include(p => p.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(p => p.Id == id);
                if (prescription != null)
                    ViewBag.PatientName = $"{prescription.Admission.Patient.FirstName} {prescription.Admission.Patient.LastName}";
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active)
                        .OrderBy(m => m.Name).ToListAsync(),
                    "Id", "Name", posted.MedicationId);
                return View(posted);
            }

            existing.MedicationId = posted.MedicationId;
            existing.Dosage = posted.Dosage;
            existing.Frequency = posted.Frequency;
            existing.Duration = posted.Duration;
            existing.Notes = posted.Notes;
            existing.PrescribedDate = posted.PrescribedDate;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription updated.";
            return RedirectToAction("PrescriptionsByAdmission", new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  PRESCRIPTION DETAILS
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PrescriptionDetails(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value);
            if (prescription == null) return NotFound();
            return View(prescription);
        }

        // ==================================================================
        //  SOFT DELETE PRESCRIPTION (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePrescription(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value);
            if (prescription == null) return NotFound();

            prescription.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription deactivated.";
            return RedirectToAction("PrescriptionsByAdmission", new { admissionId = prescription.AdmissionId });
        }

        // ==================================================================
        //  RESTORE PRESCRIPTION (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestorePrescription(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value);
            if (prescription == null) return NotFound();

            prescription.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription reactivated.";
            return RedirectToAction("PrescriptionsByAdmission", new { admissionId = prescription.AdmissionId });
        }

        // ==================================================================
        //  DISCHARGE PATIENT (doctor initiates discharge)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DischargePatient(int admissionId, string? dischargeInstructions)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Bed)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);

            if (admission == null) return NotFound();

            admission.IsActive = Status.Inactive;
            admission.DischargeDate = DateTime.Now;

            if (admission.Bed != null)
                admission.Bed.IsOccupied = false;

            if (!string.IsNullOrWhiteSpace(dischargeInstructions))
            {
                var dischargeVisit = new DoctorVisit
                {
                    AdmissionId = admissionId,
                    DoctorId = doctorId.Value,
                    VisitDate = DateTime.Now,
                    Instructions = dischargeInstructions,
                    Notes = "Discharge ordered by doctor",
                    IsContactRecord = false,
                    IsActive = Status.Active
                };
                _context.DoctorVisits.Add(dischargeVisit);
            }

            await _context.SaveChangesAsync();

            try
            {
                var admissionWithAdmin = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == admissionId);

                if (admissionWithAdmin?.CreatedByWardAdminId != null)
                {
                    string doctorName = (await _context.Employees.FindAsync(doctorId))?.FullName ?? "Doctor";
                    string patientName = admissionWithAdmin.Patient.FullName;
                    string notificationMsg = $"{doctorName} has initiated discharge for patient {patientName}.";
                    if (!string.IsNullOrWhiteSpace(dischargeInstructions))
                        notificationMsg += $" Instructions: {dischargeInstructions}";

                    string link = Url.Action("Details", "WardAdmin", new { id = admissionId });

                    await _notifService.NotifyUserAsync(
                        admissionWithAdmin.CreatedByWardAdminId.Value,
                        "Employee",
                        notificationMsg,
                        link);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Patient discharged successfully.";
            return RedirectToAction("MyPatients");
        }
    }
}