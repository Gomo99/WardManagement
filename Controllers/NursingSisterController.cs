using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "NURSINGSISTER")]
    public class NursingSisterController : Controller
    {
        private readonly WardDbContext _context;

        public NursingSisterController(WardDbContext context)
        {
            _context = context;
        }

        private int? GetCurrentNursingSisterId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.NURSINGSISTER.ToString())
                return null;
            return id;
        }

        // ---------------------------------------------------------------
        //  DASHBOARD
        // ---------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            ViewBag.ActivePatients = await _context.Admissions.CountAsync(a => a.IsActive == Status.Active);
            ViewBag.AdministeredToday = await _context.MedicationAdministrations.CountAsync(ma => ma.DateAdministered.Date == DateTime.Today && ma.IsActive == Status.Active);
            return View();
        }

        // ===============================================================
        //  VIEW ADMITTED PATIENTS
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> Patients()
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => a.IsActive == Status.Active)
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();
            return View(admissions);
        }

        // ===============================================================
        //  MEDICATION ADMINISTRATION LIST FOR A SPECIFIC ADMISSION
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> MedicationAdministrationsByAdmission(int admissionId)
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            var administrations = await _context.MedicationAdministrations
                .Include(ma => ma.Medication)
                .Where(ma => ma.AdmissionId == admissionId && ma.IsActive == Status.Active)
                .OrderByDescending(ma => ma.DateAdministered)
                .ToListAsync();
            return View(administrations);
        }

        // ===============================================================
        //  ADMINISTER MEDICATION – GET
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> AdministerMedication(int admissionId)
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            // Nursing Sister can administer ALL active medications (including schedule 5+)
            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active)
                    .OrderBy(m => m.Name)
                    .ToListAsync(),
                "Id", "Name");

            return View(new MedicationAdministration
            {
                AdmissionId = admissionId,
                DateAdministered = DateTime.Now
            });
        }

        // ===============================================================
        //  ADMINISTER MEDICATION – POST
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdministerMedication(MedicationAdministration administration)
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");

            // Validate admission
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == administration.AdmissionId && a.IsActive == Status.Active);

            if (!ModelState.IsValid)
            {
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = administration.AdmissionId;
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active)
                        .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", administration.MedicationId);
                return View(administration);
            }

            if (admission == null)
            {
                ModelState.AddModelError("", "Invalid admission.");
                return View(administration);
            }

            // No schedule restriction – Nursing Sister can administer any medication
            var medication = await _context.Medications.FindAsync(administration.MedicationId);
            if (medication == null || medication.IsActive != Status.Active)
            {
                ModelState.AddModelError("", "Invalid medication.");
                ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = administration.AdmissionId;
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active)
                        .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", administration.MedicationId);
                return View(administration);
            }

            administration.IsActive = Status.Active;
            _context.MedicationAdministrations.Add(administration);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication administered successfully.";
            return RedirectToAction(nameof(MedicationAdministrationsByAdmission), new { admissionId = administration.AdmissionId });
        }

        // ===============================================================
        //  EDIT MEDICATION ADMINISTRATION – GET
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> EditMedicationAdministration(int id)
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var administration = await _context.MedicationAdministrations
                .Include(ma => ma.Admission).ThenInclude(a => a.Patient)
                .Include(ma => ma.Medication)
                .FirstOrDefaultAsync(ma => ma.Id == id && ma.IsActive == Status.Active);
            if (administration == null) return NotFound();

            ViewBag.PatientName = $"{administration.Admission.Patient.FirstName} {administration.Admission.Patient.LastName}";
            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active)
                    .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", administration.MedicationId);
            return View(administration);
        }

        // ===============================================================
        //  EDIT MEDICATION ADMINISTRATION – POST
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMedicationAdministration(int id, MedicationAdministration posted)
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");

            var existing = await _context.MedicationAdministrations
                .Include(ma => ma.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(ma => ma.Id == id && ma.IsActive == Status.Active);
            if (existing == null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.PatientName = $"{existing.Admission.Patient.FirstName} {existing.Admission.Patient.LastName}";
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active)
                        .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", posted.MedicationId);
                return View(posted);
            }

            // No schedule restriction – Nursing Sister can change to any medication
            var newMed = await _context.Medications.FindAsync(posted.MedicationId);
            if (newMed == null || newMed.IsActive != Status.Active)
            {
                ModelState.AddModelError("", "Invalid medication.");
                ViewBag.PatientName = $"{existing.Admission.Patient.FirstName} {existing.Admission.Patient.LastName}";
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active)
                        .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", posted.MedicationId);
                return View(posted);
            }

            existing.MedicationId = posted.MedicationId;
            existing.Dosage = posted.Dosage;
            existing.DateAdministered = posted.DateAdministered;
            existing.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Administration record updated.";
            return RedirectToAction(nameof(MedicationAdministrationsByAdmission), new { admissionId = existing.AdmissionId });
        }

        // ===============================================================
        //  MEDICATION ADMINISTRATION DETAILS
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> DetailsMedicationAdministration(int id)
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var administration = await _context.MedicationAdministrations
                .Include(ma => ma.Admission).ThenInclude(a => a.Patient)
                .Include(ma => ma.Medication)
                .FirstOrDefaultAsync(ma => ma.Id == id);
            if (administration == null) return NotFound();
            return View(administration);
        }

        // ===============================================================
        //  SOFT DELETE MEDICATION ADMINISTRATION – POST
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMedicationAdministration(int id)
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var administration = await _context.MedicationAdministrations.FindAsync(id);
            if (administration == null) return NotFound();

            administration.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Administration record deactivated.";
            return RedirectToAction(nameof(MedicationAdministrationsByAdmission), new { admissionId = administration.AdmissionId });
        }

        // ===============================================================
        //  RESTORE MEDICATION ADMINISTRATION – POST
        // ===============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreMedicationAdministration(int id)
        {
            int? nurseId = GetCurrentNursingSisterId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var administration = await _context.MedicationAdministrations.FindAsync(id);
            if (administration == null) return NotFound();

            administration.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Administration record reactivated.";
            return RedirectToAction(nameof(MedicationAdministrationsByAdmission), new { admissionId = administration.AdmissionId });
        }
    }
}