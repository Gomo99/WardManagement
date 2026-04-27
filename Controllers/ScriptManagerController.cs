using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    public class ScriptManagerController : Controller
    {
        private readonly WardDbContext _context;

        public ScriptManagerController(WardDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {

            ViewBag.NewScriptsCount = await _context.Prescriptions.CountAsync(p => p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.New);
            ViewBag.ForwardedCount = await _context.Prescriptions.CountAsync(p => p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.ForwardedToPharmacy);
            ViewBag.DeliveredCount = await _context.Prescriptions.CountAsync(p => p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.Delivered);
            return View();
        }

        // ==================================================================
        //  VIEW NEW SCRIPTS
        // ==================================================================
        public async Task<IActionResult> NewScripts()
        {
            var newPrescriptions = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.New)
                .OrderBy(p => p.PrescribedDate)
                .ToListAsync();
            return View(newPrescriptions);
        }

        // ==================================================================
        //  FILTERED VIEWS (optional)
        // ==================================================================
        public async Task<IActionResult> ForwardedScripts()
        {
            var list = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.ForwardedToPharmacy)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> DeliveredScripts()
        {
            var list = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.Delivered)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();
            return View(list);
        }

        // ==================================================================
        //  CREATE PRESCRIPTION – GET
        // ==================================================================
        [HttpGet]
        public IActionResult Create()
        {
            // Populate dropdowns for Admission and Medication
            ViewBag.Admissions = new SelectList(
                _context.Admissions
                    .Include(a => a.Patient)
                    .Where(a => a.IsActive == Status.Active)
                    .OrderBy(a => a.Patient.LastName)
                    .Select(a => new
                    {
                        Id = a.Id,
                        Display = a.Patient.LastName + ", " + a.Patient.FirstName + " (Adm #" + a.Id + ")"
                    })
                    .ToList(),
                "Id", "Display");

            ViewBag.Medications = new SelectList(
                _context.Medications.Where(m => m.IsActive == Status.Active).OrderBy(m => m.Name),
                "Id", "Name");

            return View(new Prescription
            {
                PrescribedDate = DateTime.Now,
                ScriptStatus = ScriptStatus.New
            });
        }

        // ==================================================================
        //  CREATE PRESCRIPTION – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Prescription prescription)
        {
            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");

            if (!ModelState.IsValid)
            {
                ViewBag.Admissions = new SelectList(
                    _context.Admissions
                        .Include(a => a.Patient)
                        .Where(a => a.IsActive == Status.Active)
                        .OrderBy(a => a.Patient.LastName)
                        .Select(a => new { Id = a.Id, Display = a.Patient.LastName + ", " + a.Patient.FirstName + " (Adm #" + a.Id + ")" }),
                    "Id", "Display", prescription.AdmissionId);
                ViewBag.Medications = new SelectList(
                    _context.Medications.Where(m => m.IsActive == Status.Active).OrderBy(m => m.Name),
                    "Id", "Name", prescription.MedicationId);
                return View(prescription);
            }

            // Verify that the selected admission is active
            var admission = await _context.Admissions.FindAsync(prescription.AdmissionId);
            if (admission == null || admission.IsActive != Status.Active)
            {
                ModelState.AddModelError("AdmissionId", "Invalid admission.");
                return View(prescription);
            }

            prescription.IsActive = Status.Active;
            prescription.ScriptStatus = ScriptStatus.New;
            _context.Prescriptions.Add(prescription);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription created.";
            return RedirectToAction(nameof(AllScripts));
        }

        // ==================================================================
        //  EDIT PRESCRIPTION – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);
            if (prescription == null) return NotFound();

            // Only allow editing if status is New (not yet forwarded)
            if (prescription.ScriptStatus != ScriptStatus.New)
            {
                TempData["ErrorMessage"] = "Only prescriptions with status 'New' can be edited.";
                return RedirectToAction(nameof(AllScripts));
            }

            ViewBag.Admissions = new SelectList(
                _context.Admissions
                    .Include(a => a.Patient)
                    .Where(a => a.IsActive == Status.Active)
                    .OrderBy(a => a.Patient.LastName)
                    .Select(a => new { Id = a.Id, Display = a.Patient.LastName + ", " + a.Patient.FirstName + " (Adm #" + a.Id + ")" }),
                "Id", "Display", prescription.AdmissionId);
            ViewBag.Medications = new SelectList(
                _context.Medications.Where(m => m.IsActive == Status.Active).OrderBy(m => m.Name),
                "Id", "Name", prescription.MedicationId);

            return View(prescription);
        }

        // ==================================================================
        //  EDIT PRESCRIPTION – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Prescription posted)
        {
            if (id != posted.Id) return BadRequest();

            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");
            ModelState.Remove("ScriptStatus");

            if (!ModelState.IsValid)
            {
                // Repopulate dropdowns with selected values
                ViewBag.Admissions = new SelectList(
                    _context.Admissions
                        .Include(a => a.Patient)
                        .Where(a => a.IsActive == Status.Active)
                        .OrderBy(a => a.Patient.LastName)
                        .Select(a => new { Id = a.Id, Display = a.Patient.LastName + ", " + a.Patient.FirstName + " (Adm #" + a.Id + ")" }),
                    "Id", "Display", posted.AdmissionId);
                ViewBag.Medications = new SelectList(
                    _context.Medications.Where(m => m.IsActive == Status.Active).OrderBy(m => m.Name),
                    "Id", "Name", posted.MedicationId);
                return View(posted);
            }

            var prescription = await _context.Prescriptions.FindAsync(id);
            if (prescription == null || prescription.IsActive != Status.Active)
                return NotFound();

            if (prescription.ScriptStatus != ScriptStatus.New)
            {
                TempData["ErrorMessage"] = "Only prescriptions with status 'New' can be edited.";
                return RedirectToAction(nameof(AllScripts));
            }

            prescription.AdmissionId = posted.AdmissionId;
            prescription.MedicationId = posted.MedicationId;
            prescription.Dosage = posted.Dosage;
            prescription.Frequency = posted.Frequency;
            prescription.Duration = posted.Duration;
            prescription.Notes = posted.Notes;
            prescription.PrescribedDate = posted.PrescribedDate;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription updated.";
            return RedirectToAction(nameof(AllScripts));
        }

        // ==================================================================
        //  FORWARD TO PHARMACY
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForwardToPharmacy(int id)
        {
            var prescription = await _context.Prescriptions.FindAsync(id);
            if (prescription == null || prescription.IsActive != Status.Active)
                return NotFound();

            if (prescription.ScriptStatus != ScriptStatus.New)
            {
                TempData["ErrorMessage"] = "This prescription has already been processed.";
                return RedirectToAction(nameof(NewScripts));
            }

            prescription.ScriptStatus = ScriptStatus.ForwardedToPharmacy;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription forwarded to pharmacy.";
            return RedirectToAction(nameof(NewScripts));
        }

        // ==================================================================
        //  RECEIVE & VERIFY SCRIPT – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ReceiveScript(int id)
        {
            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);

            if (prescription == null) return NotFound();

            if (prescription.ScriptStatus != ScriptStatus.ForwardedToPharmacy)
            {
                TempData["ErrorMessage"] = "This prescription cannot be received at this stage.";
                return RedirectToAction(nameof(AllScripts));
            }

            return View(prescription);
        }

        // ==================================================================
        //  RECEIVE & VERIFY SCRIPT – POST (confirm delivery)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveScriptConfirmed(int id)
        {
            var prescription = await _context.Prescriptions.FindAsync(id);
            if (prescription == null || prescription.IsActive != Status.Active)
                return NotFound();

            if (prescription.ScriptStatus != ScriptStatus.ForwardedToPharmacy)
            {
                TempData["ErrorMessage"] = "Prescription must be forwarded to pharmacy first.";
                return RedirectToAction(nameof(AllScripts));
            }

            prescription.ScriptStatus = ScriptStatus.Delivered;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication verified and received on ward.";
            return RedirectToAction(nameof(AllScripts));
        }

        // ==================================================================
        //  VIEW ALL SCRIPTS
        // ==================================================================
        public async Task<IActionResult> AllScripts()
        {
            var prescriptions = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.IsActive == Status.Active)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();
            return View(prescriptions);
        }

        // ==================================================================
        //  DETAILS
        // ==================================================================
        public async Task<IActionResult> Details(int id)
        {
            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);
            if (prescription == null) return NotFound();
            return View(prescription);
        }

        // ==================================================================
        //  SOFT DELETE (cancel)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteScript(int id)
        {
            var prescription = await _context.Prescriptions.FindAsync(id);
            if (prescription == null) return NotFound();

            prescription.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription deactivated.";
            return RedirectToAction(nameof(AllScripts));
        }

        // ==================================================================
        //  RESTORE
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreScript(int id)
        {
            var prescription = await _context.Prescriptions.FindAsync(id);
            if (prescription == null) return NotFound();

            prescription.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription reactivated.";
            return RedirectToAction(nameof(AllScripts));
        }
    }
}