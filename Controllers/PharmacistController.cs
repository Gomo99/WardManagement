using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;   // for INotificationService

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "PHARMACIST")]

    public class PharmacistController : Controller
    {
        private readonly WardDbContext _context;
        private readonly INotificationService _notifService;

        public PharmacistController(WardDbContext context, INotificationService notifService)
        {
            _context = context;
            _notifService = notifService;
        }

        // ------------------------------------------------------------------
        //  HELPER – get current Pharmacist's EmployeeID from login
        // ------------------------------------------------------------------
        private int? GetCurrentPharmacistId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.PHARMACIST.ToString())
                return null;
            return id;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? pharmacistId = GetCurrentPharmacistId();
            if (pharmacistId == null) return RedirectToAction("Login", "Account");

            ViewBag.PendingDispensing = await _context.Prescriptions
                .CountAsync(p => p.IsActive == Status.Active &&
                                 p.ScriptStatus == ScriptStatus.ForwardedToPharmacy);
            ViewBag.DispensedToday = await _context.Prescriptions
                .CountAsync(p => p.IsActive == Status.Active &&
                                 p.ScriptStatus == ScriptStatus.Dispensed &&
                                 p.PharmacistId == pharmacistId &&
                                 p.PrescribedDate.Date == DateTime.Today);
            return View();
        }

        // ==================================================================
        //  VIEW PRESCRIPTIONS FORWARDED TO PHARMACY (ready to dispense)
        // ==================================================================
        public async Task<IActionResult> Index()
        {
            int? pharmacistId = GetCurrentPharmacistId();
            if (pharmacistId == null) return RedirectToAction("Login", "Account");

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .Where(p => p.IsActive == Status.Active &&
                            p.ScriptStatus == ScriptStatus.ForwardedToPharmacy)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();

            return View(prescriptions);
        }

        // ==================================================================
        //  DISPENSE MEDICATION – GET (verification step)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> Dispense(int id)
        {
            int? pharmacistId = GetCurrentPharmacistId();
            if (pharmacistId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id &&
                                         p.IsActive == Status.Active &&
                                         p.ScriptStatus == ScriptStatus.ForwardedToPharmacy);
            if (prescription == null) return NotFound();

            return View(prescription);
        }

        // ==================================================================
        //  DISPENSE MEDICATION – POST (confirm dispensing)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dispense(int id, string? batchNumber, int quantityDispensed = 0)
        {
            int? pharmacistId = GetCurrentPharmacistId();
            if (pharmacistId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id &&
                                         p.IsActive == Status.Active &&
                                         p.ScriptStatus == ScriptStatus.ForwardedToPharmacy);
            if (prescription == null) return NotFound();

            // Update prescription status
            prescription.ScriptStatus = ScriptStatus.Dispensed;
            prescription.PharmacistId = pharmacistId;   // optional, if you added the property

            // You could also record batch number and quantity dispensed in a separate table
            // For now we simply add a note (you can extend this later)
            prescription.Notes = (prescription.Notes ?? "") +
                                 $" | Dispensed by pharmacist #{pharmacistId} on {DateTime.Now:g}";
            if (!string.IsNullOrWhiteSpace(batchNumber))
                prescription.Notes += $". Batch: {batchNumber}";

            await _context.SaveChangesAsync();

            // --------------- NOTIFICATION TO SCRIPT MANAGER ---------------
            try
            {
                if (prescription.ScriptManagerId.HasValue)
                {
                    string pharmacistName = (await _context.Employees.FindAsync(pharmacistId))?.FullName ?? "Pharmacist";
                    string patientName = prescription.Admission?.Patient?.FullName ?? "a patient";
                    string medName = prescription.Medication?.Name ?? "medication";
                    string link = Url.Action("AllScripts", "ScriptManager");

                    await _notifService.NotifyUserAsync(
                        prescription.ScriptManagerId.Value,
                        "Employee",
                        $"{pharmacistName} has dispensed {medName} for {patientName}. It is ready for delivery.",
                        link);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Medication dispensed successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ==================================================================
        //  VIEW DETAILS OF A PRESCRIPTION (optional)
        // ==================================================================
        public async Task<IActionResult> Details(int id)
        {
            int? pharmacistId = GetCurrentPharmacistId();
            if (pharmacistId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive == Status.Active);
            if (prescription == null) return NotFound();

            return View(prescription);
        }
    }
}