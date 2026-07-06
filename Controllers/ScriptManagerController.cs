using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "SCRIPTMANAGER")]
    public class ScriptManagerController : Controller
    {
        private readonly WardDbContext _context;
        private readonly INotificationService _notifService;

        public ScriptManagerController(WardDbContext context, INotificationService notifService)
        {
            _context = context;
            _notifService = notifService;
        }

        private int? GetCurrentScriptManagerId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.SCRIPTMANAGER.ToString())
                return null;
            return id;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ViewBag.NewScriptsCount = await _context.Prescriptions.CountAsync(p =>
                p.ScriptManagerId == managerId && p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.New);
            ViewBag.ForwardedCount = await _context.Prescriptions.CountAsync(p =>
                p.ScriptManagerId == managerId && p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.ForwardedToPharmacy);
            ViewBag.DeliveredCount = await _context.Prescriptions.CountAsync(p =>
                p.ScriptManagerId == managerId && p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.Delivered);
            ViewBag.DispensedCount = await _context.Prescriptions.CountAsync(p =>
                p.ScriptManagerId == managerId && p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.Dispensed);
            return View();
        }

        // ==================================================================
        //  VIEW NEW SCRIPTS
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> NewScripts()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var newPrescriptions = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.ScriptManagerId == managerId && p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.New)
                .OrderByDescending(p => p.IsStat)
                .ThenBy(p => p.PrescribedDate)
                .ToListAsync();
            return View(newPrescriptions);
        }

        // ==================================================================
        //  FILTERED VIEWS (optional)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ForwardedScripts()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var list = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.IsActive == Status.Active && p.ScriptStatus == ScriptStatus.ForwardedToPharmacy)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();
            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> DeliveredScripts()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

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
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

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

            var admission = await _context.Admissions.FindAsync(prescription.AdmissionId);
            if (admission == null || admission.IsActive != Status.Active)
            {
                ModelState.AddModelError("AdmissionId", "Invalid admission.");
                return View(prescription);
            }

            if (await HasActiveDuplicate(prescription.AdmissionId, prescription.MedicationId))
            {
                TempData["WarningMessage"] = "This patient already has an active prescription for the same medication.";
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
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);
            if (prescription == null) return NotFound();

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
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");
            ModelState.Remove("ScriptStatus");

            if (!ModelState.IsValid)
            {
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

            if (posted.MedicationId != prescription.MedicationId)
            {
                if (await HasActiveDuplicate(prescription.AdmissionId, posted.MedicationId, excludePrescriptionId: id))
                {
                    TempData["WarningMessage"] = "This patient already has an active prescription for the newly selected medication.";
                }
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
        //  FORWARD TO PHARMACY (with allergy check)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForwardToPharmacy(int id)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission)
                    .ThenInclude(a => a.AdmissionAllergies)
                        .ThenInclude(aa => aa.Allergy)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id
                                         && p.ScriptManagerId == managerId
                                         && p.IsActive == Status.Active
                                         && p.ScriptStatus == ScriptStatus.New);

            if (prescription == null)
            {
                TempData["ErrorMessage"] = "This prescription cannot be forwarded (may already be processed).";
                return RedirectToAction(nameof(NewScripts));
            }

            var medicationName = prescription.Medication?.Name?.Trim();
            if (!string.IsNullOrEmpty(medicationName))
            {
                var patientAllergies = prescription.Admission?.AdmissionAllergies
                    ?.Select(aa => aa.Allergy?.Name?.Trim())
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList() ?? new List<string?>();

                if (patientAllergies.Any(a => string.Equals(a, medicationName, StringComparison.OrdinalIgnoreCase)))
                {
                    TempData["ErrorMessage"] = $"Cannot forward: the patient is allergic to '{medicationName}'.";
                    return RedirectToAction(nameof(NewScripts));
                }
            }

            prescription.ScriptStatus = ScriptStatus.ForwardedToPharmacy;
            await _context.SaveChangesAsync();

            if (prescription.IsStat)
            {
                var patientName = prescription.Admission?.Patient?.FullName ?? "a patient";
                var medName = medicationName ?? "medication";
                await _notifService.NotifyRoleAsync(
                    UserRole.PHARMACIST.ToString(),
                    $"STAT prescription for {patientName}: {medName}.",
                    Url.Action("Index", "Pharmacist"));
            }

            TempData["SuccessMessage"] = "Prescription forwarded to pharmacy.";
            return RedirectToAction(nameof(NewScripts));
        }

        // ==================================================================
        //  RECEIVE & VERIFY SCRIPT – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ReceiveScript(int id)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);

            if (prescription == null) return NotFound();

            if (prescription.ScriptStatus != ScriptStatus.Dispensed)
            {
                TempData["ErrorMessage"] = "This prescription has not been dispensed yet.";
                return RedirectToAction(nameof(AllScripts));
            }

            return View(prescription);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveScriptConfirmed(int id)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions.FindAsync(id);
            if (prescription == null || prescription.IsActive != Status.Active)
                return NotFound();

            if (prescription.ScriptStatus != ScriptStatus.Dispensed)
            {
                TempData["ErrorMessage"] = "Prescription must be dispensed by pharmacy first.";
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
        [HttpGet]
        public async Task<IActionResult> AllScripts()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

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
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

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
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

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
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");
            var prescription = await _context.Prescriptions.FindAsync(id);
            if (prescription == null) return NotFound();

            prescription.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription reactivated.";
            return RedirectToAction(nameof(AllScripts));
        }

        // ==================================================================
        //  BULK FORWARD SCRIPTS TO PHARMACY
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkForwardToPharmacy(List<int> selectedIds)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (selectedIds == null || selectedIds.Count == 0)
            {
                TempData["ErrorMessage"] = "No prescriptions selected.";
                return RedirectToAction(nameof(NewScripts));
            }

            var scripts = await _context.Prescriptions
                .Where(p => selectedIds.Contains(p.Id)
                            && p.ScriptManagerId == managerId
                            && p.IsActive == Status.Active
                            && p.ScriptStatus == ScriptStatus.New)
                .ToListAsync();

            if (scripts.Count == 0)
            {
                TempData["ErrorMessage"] = "None of the selected prescriptions can be forwarded (they may already be processed or not assigned to you).";
                return RedirectToAction(nameof(NewScripts));
            }

            foreach (var script in scripts)
            {
                script.ScriptStatus = ScriptStatus.ForwardedToPharmacy;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{scripts.Count} prescription(s) forwarded to pharmacy.";
            return RedirectToAction(nameof(NewScripts));
        }

        // ==================================================================
        //  TOGGLE URGENT (STAT) FLAG
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStat(int id)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var script = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == id
                                          && p.ScriptManagerId == managerId
                                          && p.IsActive == Status.Active
                                          && p.ScriptStatus == ScriptStatus.New);
            if (script == null) return NotFound();

            script.IsStat = !script.IsStat;
            await _context.SaveChangesAsync();

            if (script.IsStat)
            {
                var medName = (await _context.Medications.FindAsync(script.MedicationId))?.Name ?? "medication";
                var patientName = (await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == script.AdmissionId))
                    ?.Patient?.FullName ?? "a patient";

                await _notifService.NotifyRoleAsync(
                    UserRole.PHARMACIST.ToString(),
                    $"STAT prescription for {patientName}: {medName}. Please prepare immediately.",
                    Url.Action("Index", "Pharmacist"));
            }

            TempData["SuccessMessage"] = script.IsStat
                ? "Prescription marked as STAT."
                : "Prescription marked as routine.";
            return RedirectToAction(nameof(NewScripts));
        }

        // ==================================================================
        //  REJECT DISPENSED MEDICATION
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectScript(int id, string rejectionReason)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                TempData["ErrorMessage"] = "Please provide a reason for rejection.";
                return RedirectToAction(nameof(ReceiveScript), new { id });
            }

            var prescription = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == id
                                          && p.IsActive == Status.Active
                                          && p.ScriptStatus == ScriptStatus.Dispensed);
            if (prescription == null)
            {
                TempData["ErrorMessage"] = "Prescription not found or not in a state that can be rejected.";
                return RedirectToAction(nameof(AllScripts));
            }

            prescription.ScriptStatus = ScriptStatus.Rejected;
            prescription.Notes = (prescription.Notes ?? "") +
                                 $" | Rejected by Script Manager on {DateTime.Now:g}. Reason: {rejectionReason}";
            await _context.SaveChangesAsync();

            try
            {
                if (prescription.PharmacistId.HasValue)
                {
                    string managerName = (await _context.Employees.FindAsync(managerId))?.FullName ?? "Script Manager";
                    string medName = (await _context.Medications.FindAsync(prescription.MedicationId))?.Name ?? "medication";
                    string msg = $"{managerName} rejected the dispensed medication '{medName}'. Reason: {rejectionReason}";

                    await _notifService.NotifyUserAsync(
                        prescription.PharmacistId.Value,
                        "Employee",
                        msg,
                        Url.Action("Details", "Pharmacist", new { id = prescription.Id }));
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Medication rejected. The pharmacist has been notified.";
            return RedirectToAction(nameof(AllScripts));
        }

        // ==================================================================
        //  PRINT PRESCRIPTION LABEL
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PrintLabel(int id)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);

            if (prescription == null) return NotFound();

            return View(prescription);
        }

        // ==================================================================
        //  SCRIPTS READY FOR COLLECTION (DISPENSED)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ReadyForCollection()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var list = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.ScriptManagerId == managerId
                            && p.IsActive == Status.Active
                            && p.ScriptStatus == ScriptStatus.Dispensed)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();

            return View(list);
        }

        // ==================================================================
        //  PRIVATE HELPERS
        // ==================================================================
        private async Task<bool> HasActiveDuplicate(int admissionId, int medicationId, int? excludePrescriptionId = null)
        {
            var activeStatuses = new[] {
                ScriptStatus.New,
                ScriptStatus.ForwardedToPharmacy,
                ScriptStatus.Dispensed
            };

            var query = _context.Prescriptions
                .Where(p => p.AdmissionId == admissionId
                            && p.MedicationId == medicationId
                            && p.IsActive == Status.Active
                            && activeStatuses.Contains(p.ScriptStatus));

            if (excludePrescriptionId.HasValue)
                query = query.Where(p => p.Id != excludePrescriptionId.Value);

            return await query.AnyAsync();
        }
    }
}