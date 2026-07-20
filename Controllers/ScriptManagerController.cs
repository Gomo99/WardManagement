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
        //  DASHBOARD – Enhanced overview
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        //  DASHBOARD – Enhanced overview (with daily summary)
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var baseQuery = _context.Prescriptions
                .Where(p => p.ScriptManagerId == managerId && p.IsActive == Status.Active);

            // Overall stats (unchanged)
            ViewBag.NewScriptsCount = await baseQuery.CountAsync(p => p.ScriptStatus == ScriptStatus.New);
            ViewBag.ForwardedCount = await baseQuery.CountAsync(p => p.ScriptStatus == ScriptStatus.ForwardedToPharmacy);
            ViewBag.DispensedCount = await baseQuery.CountAsync(p => p.ScriptStatus == ScriptStatus.Dispensed);
            ViewBag.DeliveredCount = await baseQuery.CountAsync(p => p.ScriptStatus == ScriptStatus.Delivered);
            ViewBag.RejectedCount = await baseQuery.CountAsync(p => p.ScriptStatus == ScriptStatus.Rejected);

            // Delayed: not delivered/rejected, prescribed more than 3 days ago
            var threeDaysAgo = DateTime.Now.AddDays(-3);
            ViewBag.DelayedCount = await baseQuery
                .Where(p => p.ScriptStatus != ScriptStatus.Delivered
                         && p.ScriptStatus != ScriptStatus.Rejected
                         && p.PrescribedDate < threeDaysAgo)
                .CountAsync();

            // Priority counts
            ViewBag.EmergencyCount = await baseQuery.CountAsync(p => p.Priority == PrescriptionPriority.Emergency);
            ViewBag.UrgentCount = await baseQuery.CountAsync(p => p.Priority == PrescriptionPriority.Urgent);
            ViewBag.NormalCount = await baseQuery.CountAsync(p => p.Priority == PrescriptionPriority.Normal);
            ViewBag.RoutineCount = await baseQuery.CountAsync(p => p.Priority == PrescriptionPriority.Routine);

            // ---------- Daily Summary (Today) ----------
            var today = DateTime.Today;

            // Prescriptions created today (any status)
            ViewBag.TodayNew = await baseQuery
                .Where(p => p.PrescribedDate.Date == today)
                .CountAsync();

            // Processed today = any status beyond New (Forwarded, Dispensed, PartiallyDelivered, Delivered)
            // and prescribed today. (Processed means it has moved forward)
            var processedStatuses = new[] {
        ScriptStatus.ForwardedToPharmacy,
        ScriptStatus.Dispensed,
        ScriptStatus.PartiallyDelivered,
        ScriptStatus.Delivered
    };
            ViewBag.TodayProcessed = await baseQuery
                .Where(p => p.PrescribedDate.Date == today && processedStatuses.Contains(p.ScriptStatus))
                .CountAsync();

            // Delivered today (verified today)
            ViewBag.TodayDelivered = await baseQuery
                .Where(p => p.ScriptStatus == ScriptStatus.Delivered && p.VerifiedAt.HasValue && p.VerifiedAt.Value.Date == today)
                .CountAsync();

            // Pending today = not delivered and not rejected, prescribed today
            ViewBag.TodayPending = await baseQuery
                .Where(p => p.PrescribedDate.Date == today
                         && p.ScriptStatus != ScriptStatus.Delivered
                         && p.ScriptStatus != ScriptStatus.Rejected)
                .CountAsync();

            // Rejected today (prescribed today and rejected)
            ViewBag.TodayRejected = await baseQuery
                .Where(p => p.PrescribedDate.Date == today && p.ScriptStatus == ScriptStatus.Rejected)
                .CountAsync();

            return View();
        }

        // ==================================================================
        //  VIEW NEW SCRIPTS – with allergy warnings
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> NewScripts()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var newPrescriptions = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Admission).ThenInclude(a => a.AdmissionAllergies)
                    .ThenInclude(aa => aa.Allergy)
                .Include(p => p.Medication)
                .Where(p => p.ScriptManagerId == managerId
                            && p.IsActive == Status.Active
                            && p.ScriptStatus == ScriptStatus.New)
                .OrderByDescending(p => p.IsStat)
                .ThenByDescending(p => p.Priority == PrescriptionPriority.Emergency ? 4 :
                                      p.Priority == PrescriptionPriority.Urgent ? 3 :
                                      p.Priority == PrescriptionPriority.Normal ? 2 : 1)
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
        public async Task<IActionResult> Create(Prescription prescription, bool confirmDuplicate = false)
        {
            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");

            if (!ModelState.IsValid)
            {
                // Re‑populate dropdowns on error
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
                // Re‑populate dropdowns
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

            // Duplicate check
            bool isDuplicate = await HasActiveDuplicate(prescription.AdmissionId, prescription.MedicationId);
            if (isDuplicate && !confirmDuplicate)
            {
                ModelState.AddModelError("MedicationId", "This patient already has an active prescription for the same medication. Check 'I want to continue' to override.");
                // Re‑populate dropdowns
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

            // Warn but still create if duplicate is overridden
            if (isDuplicate)
            {
                TempData["WarningMessage"] = "This patient already has an active prescription for the same medication. A new prescription has been created.";
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
        //  FORWARD TO PHARMACY (now records forwarding details)
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

            // Forward the prescription
            prescription.ScriptStatus = ScriptStatus.ForwardedToPharmacy;
            prescription.ForwardedAt = DateTime.Now;
            prescription.ForwardedByScriptManagerId = managerId;

            await _context.SaveChangesAsync();

            // STAT notification
            if (prescription.IsStat)
            {
                var patientName = prescription.Admission?.Patient?.FullName ?? "a patient";
                var medName = medicationName ?? "medication";
                await _notifService.NotifyRoleAsync(
                    UserRole.PHARMACIST.ToString(),
                    $"STAT prescription for {patientName}: {medName}.",
                    Url.Action("Index", "Pharmacist"));
            }

            TempData["SuccessMessage"] = $"Prescription forwarded to pharmacy at {DateTime.Now:HH:mm}.";
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

            if (prescription.ScriptStatus != ScriptStatus.Dispensed && prescription.ScriptStatus != ScriptStatus.PartiallyDelivered)
            {
                TempData["ErrorMessage"] = "This prescription is not available for receiving.";
                return RedirectToAction(nameof(AllScripts));
            }

            ViewBag.Remaining = prescription.QuantityPrescribed - prescription.QuantityReceived;
            return View(prescription);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveScriptConfirmed(int id, int quantityReceived, DateTime? expiryDate, string? batchNumber)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);

            if (prescription == null) return NotFound();

            if (prescription.ScriptStatus != ScriptStatus.Dispensed && prescription.ScriptStatus != ScriptStatus.PartiallyDelivered)
            {
                TempData["ErrorMessage"] = "Prescription must be dispensed or partially delivered.";
                return RedirectToAction(nameof(AllScripts));
            }

            if (quantityReceived <= 0)
            {
                TempData["ErrorMessage"] = "Quantity must be positive.";
                return RedirectToAction(nameof(ReceiveScript), new { id });
            }

            int newTotal = prescription.QuantityReceived + quantityReceived;
            if (newTotal > prescription.QuantityPrescribed)
            {
                TempData["ErrorMessage"] = $"Cannot receive more than prescribed ({prescription.QuantityPrescribed}). Already received {prescription.QuantityReceived}.";
                return RedirectToAction(nameof(ReceiveScript), new { id });
            }

            // Update received quantity
            prescription.QuantityReceived = newTotal;

            // Update status
            if (newTotal >= prescription.QuantityPrescribed)
            {
                prescription.ScriptStatus = ScriptStatus.Delivered;
            }
            else
            {
                prescription.ScriptStatus = ScriptStatus.PartiallyDelivered;
            }

            // Record verification details (overwrite each time? Or keep first? We'll store latest)
            prescription.ExpiryDate = expiryDate;
            prescription.BatchNumber = batchNumber;
            prescription.VerifiedAt = DateTime.Now;
            prescription.VerifiedByEmployeeId = managerId;

            await _context.SaveChangesAsync();

            int remaining = prescription.QuantityPrescribed - newTotal;
            TempData["SuccessMessage"] = $"Received {quantityReceived} unit(s). Total: {newTotal}/{prescription.QuantityPrescribed}. Remaining: {remaining}.";
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
        //  PRESCRIPTION TIMELINE – visual journey
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> Timeline(int id)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);

            if (prescription == null) return NotFound();

            // Define steps and their completion logic
            var steps = new List<(string Step, int Order)>
    {
        ("Doctor Prescribed", 0),
        ("Script Manager Received", 1),
        ("Forwarded to Pharmacy", 2),
        ("Dispensed by Pharmacy", 3),
        ("Delivered to Ward", 4)
    };

            // Determine the current order based on ScriptStatus
            int currentOrder = prescription.ScriptStatus switch
            {
                ScriptStatus.New => 1,              // up to Received
                ScriptStatus.ForwardedToPharmacy => 2,
                ScriptStatus.Dispensed => 3,
                ScriptStatus.Delivered => 4,
                ScriptStatus.Rejected => -1,       // rejected is a special case
                _ => 0
            };

            var timeline = new List<TimelineEntry>();

            foreach (var (stepName, order) in steps)
            {
                string status;
                string? dateStr = null;

                if (order < currentOrder)
                {
                    status = "Completed";
                    // try to get a date if available
                    dateStr = order switch
                    {
                        0 => prescription.PrescribedDate.ToString("dd MMM yyyy HH:mm"),
                        1 => prescription.PrescribedDate.ToString("dd MMM yyyy HH:mm"),  // received when created
                                                                                         // For other steps we don't have exact timestamps, so we leave blank or use the delivered date for step 4
                        4 => prescription.DeliveredAt?.ToString("dd MMM yyyy HH:mm"),   // if exists, else null
                        _ => null
                    };
                }
                else if (order == currentOrder)
                {
                    status = "Active";
                }
                else
                {
                    status = "Pending";
                }

                string icon = status switch
                {
                    "Completed" => "fa-circle-check",
                    "Active" => "fa-spinner fa-pulse",
                    "Pending" => "fa-circle"
                };

                timeline.Add(new TimelineEntry
                {
                    Step = stepName,
                    Status = status,
                    Date = dateStr,
                    Icon = icon
                });
            }

            // If rejected, add a special step
            if (prescription.ScriptStatus == ScriptStatus.Rejected)
            {
                // mark all steps as completed up to dispensed? Actually rejection can happen after dispensed.
                // For simplicity, we'll add rejection as the final step.
                timeline.Add(new TimelineEntry
                {
                    Step = "Rejected by Script Manager",
                    Status = "Completed",
                    Date = prescription.Notes?.Contains("Rejected") == true ? prescription.Notes.Split('|').LastOrDefault()?.Trim() : null,
                    Icon = "fa-circle-xmark"
                });
            }

            ViewBag.PatientName = prescription.Admission?.Patient?.FullName;
            ViewBag.Medication = prescription.Medication?.Name;
            ViewBag.PrescriptionId = id;

            return View(timeline);
        }


        // ==================================================================
        //  MISSING MEDICATION ALERTS – partially delivered items
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> MissingMedicationAlerts()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var missing = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.ScriptManagerId == managerId
                            && p.IsActive == Status.Active
                            && p.ScriptStatus == ScriptStatus.PartiallyDelivered
                            && p.QuantityReceived < p.QuantityPrescribed)
                .OrderByDescending(p => p.IsStat)
                .ThenBy(p => p.PrescribedDate)
                .ToListAsync();

            return View(missing);
        }


        // ==================================================================
        //  PRINT PRESCRIPTION – pharmacy / patient copy
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PrintPrescription(int id, string? copy = null)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Admission).ThenInclude(a => a.Doctor)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);

            if (prescription == null) return NotFound();

            ViewBag.CopyType = copy ?? "pharmacy";   // default to pharmacy copy

            return View(prescription);
        }


        // ==================================================================
        //  BARCODE VERIFICATION – scanner page
        // ==================================================================
        [HttpGet]
        public IActionResult VerifyBarcode()
        {
            return View();
        }

        // ==================================================================
        //  BARCODE VERIFICATION RESULT – after scan
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> VerifyBarcodeResult(int id)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);

            if (prescription == null)
            {
                TempData["ErrorMessage"] = "Prescription not found.";
                return RedirectToAction(nameof(VerifyBarcode));
            }

            return View(prescription);
        }

        // ==================================================================
        //  REPORTS
        // ==================================================================

        // --- DAILY PRESCRIPTIONS ---
        [HttpGet]
        public async Task<IActionResult> DailyReport(DateTime? fromDate, DateTime? toDate)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var from = fromDate ?? DateTime.Today.AddDays(-7);
            var to = toDate ?? DateTime.Today;

            var data = await _context.Prescriptions
                .Where(p => p.PrescribedDate.Date >= from.Date && p.PrescribedDate.Date <= to.Date && p.IsActive == Status.Active)
                .GroupBy(p => p.PrescribedDate.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date)
                .ToListAsync();

            ViewBag.From = from; ViewBag.To = to;
            return View(data);
        }

        // --- WEEKLY PRESCRIPTIONS ---
        [HttpGet]
        public async Task<IActionResult> WeeklyReport(int? year)
        {
            int y = year ?? DateTime.Today.Year;
            var data = await _context.Prescriptions
                .Where(p => p.PrescribedDate.Year == y && p.IsActive == Status.Active)
                .GroupBy(p => new { Week = EF.Functions.DateDiffWeek(DateTime.MinValue, p.PrescribedDate) })
                .Select(g => new { WeekStart = g.Min(p => p.PrescribedDate.Date), Count = g.Count() })
                .OrderBy(x => x.WeekStart)
                .ToListAsync();

            ViewBag.Year = y;
            return View(data);
        }

        // --- MOST PRESCRIBED MEDICATIONS ---
        [HttpGet]
        public async Task<IActionResult> TopMedications(int top = 10, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var from = fromDate ?? DateTime.Today.AddMonths(-1);
            var to = toDate ?? DateTime.Today;
            var data = await _context.Prescriptions
                .Include(p => p.Medication)
                .Where(p => p.PrescribedDate.Date >= from.Date && p.PrescribedDate.Date <= to.Date && p.IsActive == Status.Active)
                .GroupBy(p => p.Medication.Name)
                .Select(g => new { Medication = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(top)
                .ToListAsync();

            ViewBag.From = from; ViewBag.To = to; ViewBag.Top = top;
            return View(data);
        }


        // --- DELAYED DELIVERIES ---
        [HttpGet]
        public async Task<IActionResult> DelayedDeliveries(int days = 3)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var threshold = DateTime.Now.AddDays(-days);
            var list = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.IsActive == Status.Active
                         && p.ScriptStatus != ScriptStatus.Delivered
                         && p.ScriptStatus != ScriptStatus.Rejected
                         && p.PrescribedDate < threshold)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();

            ViewBag.Days = days;
            return View(list);
        }

        // --- PHARMACY TURNAROUND TIME ---
        [HttpGet]
        public async Task<IActionResult> TurnaroundTime(DateTime? fromDate, DateTime? toDate)
        {
            var from = fromDate ?? DateTime.Today.AddMonths(-1);
            var to = toDate ?? DateTime.Today;

            var prescriptions = await _context.Prescriptions
                .Where(p => p.ForwardedAt.HasValue && p.VerifiedAt.HasValue
                         && p.ForwardedAt.Value.Date >= from.Date && p.ForwardedAt.Value.Date <= to.Date
                         && p.IsActive == Status.Active)
                .Select(p => new
                {
                    p.Id,
                    p.Medication.Name,
                    Forwarded = p.ForwardedAt.Value,
                    Received = p.VerifiedAt.Value,
                    Duration = EF.Functions.DateDiffHour(p.ForwardedAt.Value, p.VerifiedAt.Value)
                })
                .OrderByDescending(x => x.Duration)
                .ToListAsync();

            var avgHours = prescriptions.Any() ? prescriptions.Average(x => x.Duration) : 0;
            ViewBag.AvgHours = avgHours; ViewBag.From = from; ViewBag.To = to;
            return View(prescriptions);
        }

        // --- REJECTED PRESCRIPTIONS ---
        [HttpGet]
        public async Task<IActionResult> RejectedPrescriptions(DateTime? fromDate, DateTime? toDate)
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var from = fromDate ?? DateTime.Today.AddMonths(-1);
            var to = toDate ?? DateTime.Today;
            var list = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.ScriptStatus == ScriptStatus.Rejected
                         && p.PrescribedDate.Date >= from.Date && p.PrescribedDate.Date <= to.Date
                         && p.IsActive == Status.Active)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();

            ViewBag.From = from; ViewBag.To = to;
            return View(list);
        }

        // --- MEDICATION SHORTAGES (PARTIALLY DELIVERED / OUTSTANDING) ---
        [HttpGet]
        public async Task<IActionResult> MedicationShortages()
        {
            int? managerId = GetCurrentScriptManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var shortages = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.IsActive == Status.Active
                         && (p.ScriptStatus == ScriptStatus.PartiallyDelivered || p.QuantityReceived < p.QuantityPrescribed))
                .OrderByDescending(p => p.Priority)
                .ThenBy(p => p.PrescribedDate)
                .ToListAsync();

            return View(shortages);
        }

        // --- SCRIPTS PER DOCTOR ---
        [HttpGet]
        public async Task<IActionResult> ScriptsPerDoctor(DateTime? fromDate, DateTime? toDate)
        {
            var from = fromDate ?? DateTime.Today.AddMonths(-1);
            var to = toDate ?? DateTime.Today;

            var data = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Doctor)
                .Where(p => p.PrescribedDate.Date >= from.Date && p.PrescribedDate.Date <= to.Date && p.IsActive == Status.Active)
                .GroupBy(p => p.Admission.Doctor.FullName)
                .Select(g => new { Doctor = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            ViewBag.From = from; ViewBag.To = to;
            return View(data);
        }

        // --- SCRIPTS PER WARD ---
        [HttpGet]
        public async Task<IActionResult> ScriptsPerWard(DateTime? fromDate, DateTime? toDate)
        {
            var from = fromDate ?? DateTime.Today.AddMonths(-1);
            var to = toDate ?? DateTime.Today;

            var data = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(p => p.PrescribedDate.Date >= from.Date && p.PrescribedDate.Date <= to.Date && p.IsActive == Status.Active)
                .GroupBy(p => p.Admission.Bed.Ward.Name)
                .Select(g => new { Ward = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            ViewBag.From = from; ViewBag.To = to;
            return View(data);
        }

        // --- SCRIPTS BY PATIENT ---
        [HttpGet]
        public async Task<IActionResult> ScriptsByPatient(DateTime? fromDate, DateTime? toDate)
        {
            var from = fromDate ?? DateTime.Today.AddMonths(-1);
            var to = toDate ?? DateTime.Today;

            var data = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Where(p => p.PrescribedDate.Date >= from.Date && p.PrescribedDate.Date <= to.Date && p.IsActive == Status.Active)
                .GroupBy(p => p.Admission.Patient.FullName)
                .Select(g => new { Patient = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync();

            ViewBag.From = from; ViewBag.To = to;
            return View(data);
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