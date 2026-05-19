using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "Porter")]

    public class PorterController : Controller
    {
        private readonly WardDbContext _context;

        public PorterController(WardDbContext context)
        {
            _context = context;
        }

        private int? GetCurrentPorterId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.PORTER.ToString())
                return null;
            return id;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            ViewBag.PendingMoves = await _context.PatientMovements
                .CountAsync(m => m.PorterId == porterId &&
                                 m.MovementType == "CheckOutRequest" &&
                                 m.Timestamp == null);
            ViewBag.CompletedToday = await _context.PatientMovements
                .CountAsync(m => m.PorterId == porterId &&
                                 m.MovementType == "CheckOut" &&
                                 m.Timestamp.HasValue &&
                                 m.Timestamp.Value.Date == DateTime.Today);
            return View();
        }

        // ==================================================================
        //  PENDING MOVEMENT REQUESTS (assigned to this porter)
        // ==================================================================
        public async Task<IActionResult> MyMovements()
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var requests = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .Include(m => m.Admission.Bed).ThenInclude(b => b.Ward)
                .Where(m => m.PorterId == porterId &&
                            m.MovementType == "CheckOutRequest" &&
                            m.Timestamp == null)
                .OrderByDescending(m => m.Id)
                .ToListAsync();

            return View(requests);
        }

        // ==================================================================
        //  CONFIRM CHECK‑OUT (porter physically takes patient)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmCheckOut(int movementId)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var movement = await _context.PatientMovements
                .Include(m => m.Admission)
                .FirstOrDefaultAsync(m => m.Id == movementId &&
                                          m.PorterId == porterId &&
                                          m.MovementType == "CheckOutRequest" &&
                                          m.Timestamp == null);
            if (movement == null) return NotFound();

            // Mark as completed and change type to CheckOut
            movement.Timestamp = DateTime.Now;
            movement.MovementType = "CheckOut";
            // Update admission current location
            movement.Admission.CurrentLocation = movement.Location;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Patient moved to {movement.Location}.";
            return RedirectToAction(nameof(MyMovements));
        }

        // ==================================================================
        //  PATIENTS CURRENTLY OUT (by this porter) – ready for check‑in
        // ==================================================================
        public async Task<IActionResult> CheckInList()
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            // Find admissions where the last CheckOut movement was done by this porter
            // and the patient is still out (CurrentLocation != null)
            var outPatients = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => a.IsActive == Status.Active && a.CurrentLocation != null)
                .ToListAsync();

            // Filter to only those whose last CheckOut was by this porter
            var porterOutPatients = new List<Admission>();
            foreach (var admission in outPatients)
            {
                var lastOut = await _context.PatientMovements
                    .Where(m => m.AdmissionId == admission.Id && m.MovementType == "CheckOut")
                    .OrderByDescending(m => m.Timestamp)
                    .FirstOrDefaultAsync();

                if (lastOut != null && lastOut.PorterId == porterId)
                    porterOutPatients.Add(admission);
            }

            ViewBag.PorterId = porterId;
            return View(porterOutPatients);
        }

        // ==================================================================
        //  CONFIRM CHECK‑IN (porter returns patient to ward)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmCheckIn(int admissionId)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            if (string.IsNullOrEmpty(admission.CurrentLocation))
            {
                TempData["ErrorMessage"] = "Patient is already in the ward.";
                return RedirectToAction(nameof(CheckInList));
            }

            // Verify the last CheckOut was by this porter
            var lastOut = await _context.PatientMovements
                .Where(m => m.AdmissionId == admissionId && m.MovementType == "CheckOut")
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefaultAsync();
            if (lastOut?.PorterId != porterId)
            {
                TempData["ErrorMessage"] = "You are not the porter who checked out this patient.";
                return RedirectToAction(nameof(CheckInList));
            }

            // Record check‑in
            _context.PatientMovements.Add(new PatientMovement
            {
                AdmissionId = admissionId,
                MovementType = "CheckIn",
                Location = admission.Bed?.BedNumberWithWard ?? "Ward",
                Notes = "Returned by porter",
                PorterId = porterId,
                Timestamp = DateTime.Now
            });

            // Update admission
            admission.CurrentLocation = null;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Patient checked in successfully.";
            return RedirectToAction(nameof(CheckInList));
        }

        // ==================================================================
        //  MOVEMENT HISTORY FOR A PATIENT (anyone can view)
        // ==================================================================
        public async Task<IActionResult> History(int admissionId)
        {
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            var movements = await _context.PatientMovements
                .Include(m => m.Porter)   // to show porter name
                .Where(m => m.AdmissionId == admissionId)
                .OrderByDescending(m => m.Timestamp)
                .ToListAsync();

            return View(movements);
        }
    }
}