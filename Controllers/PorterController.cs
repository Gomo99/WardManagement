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
    [Authorize(Roles = "PORTER")]
    public class PorterController : Controller
    {
        private readonly WardDbContext _context;
        private readonly INotificationService _notifService;

        public PorterController(WardDbContext context, INotificationService notifService)
        {
            _context = context;
            _notifService = notifService;
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
        [HttpGet]
        public async Task<IActionResult> MyMovements()
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var requests = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .Include(m => m.Admission.Bed).ThenInclude(b => b.Ward)
                .Where(m => m.PorterId == porterId &&
                            m.MovementType == "CheckOutRequest" &&
                            m.Timestamp == null &&
                            m.RejectedAt == null)
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
        [HttpGet]
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
        [HttpGet]
        public async Task<IActionResult> History(int admissionId)
        {
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            var movements = await _context.PatientMovements
                .Include(m => m.Porter)
                .Where(m => m.AdmissionId == admissionId)
                .OrderByDescending(m => m.Timestamp)
                .ToListAsync();

            return View(movements);
        }

        // ==================================================================
        //  ACCEPT MOVEMENT REQUEST
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> AcceptMovement(int movementId)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var movement = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(m => m.Id == movementId
                                          && m.PorterId == porterId
                                          && m.MovementType == "CheckOutRequest"
                                          && m.Timestamp == null);
            if (movement == null) return NotFound();

            if (movement.AcceptedAt != null)
            {
                TempData["ErrorMessage"] = "This request has already been accepted.";
                return RedirectToAction(nameof(MyMovements));
            }

            ViewBag.PatientName = movement.Admission?.Patient?.FullName;
            ViewBag.Destination = movement.Location;
            return View(movement);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptMovement(int movementId, int? etaMinutes)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var movement = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(m => m.Id == movementId
                                          && m.PorterId == porterId
                                          && m.MovementType == "CheckOutRequest"
                                          && m.Timestamp == null);
            if (movement == null) return NotFound();

            if (movement.AcceptedAt != null)
            {
                TempData["ErrorMessage"] = "This request has already been accepted.";
                return RedirectToAction(nameof(MyMovements));
            }

            // Validate ETA
            if (etaMinutes.HasValue && etaMinutes.Value <= 0)
            {
                TempData["ErrorMessage"] = "ETA must be a positive number of minutes.";
                return RedirectToAction(nameof(AcceptMovement), new { movementId });
            }

            movement.AcceptedAt = DateTime.Now;
            if (etaMinutes.HasValue)
                movement.ETA = DateTime.Now.AddMinutes(etaMinutes.Value);
            else
                movement.ETA = null;

            await _context.SaveChangesAsync();

            // Optionally notify the requesting Ward Admin about acceptance + ETA
            try
            {
                int? targetAdminId = movement.RequestedByWardAdminId
                                     ?? movement.Admission?.CreatedByWardAdminId;

                if (targetAdminId.HasValue)
                {
                    string porterName = (await _context.Employees.FindAsync(porterId))?.FullName ?? "Porter";
                    string patientName = movement.Admission?.Patient?.FullName ?? "a patient";
                    string etaMsg = movement.ETA.HasValue
                        ? $" ETA: {movement.ETA:HH:mm}"
                        : "";
                    string msg = $"{porterName} has accepted the movement of {patientName} to {movement.Location}.{etaMsg}";

                    await _notifService.NotifyUserAsync(
                        targetAdminId.Value,
                        "Employee",
                        msg,
                        Url.Action("Details", "WardAdmin", new { id = movement.AdmissionId }));
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = $"Movement accepted. Patient: {movement.Admission?.Patient?.FullName}.";
            return RedirectToAction(nameof(MyMovements));
        }

        // ==================================================================
        //  REJECT MOVEMENT REQUEST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectMovement(int movementId, string reason)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Please provide a reason for rejection.";
                return RedirectToAction(nameof(MyMovements));
            }

            var movement = await _context.PatientMovements
                .Include(m => m.Admission)
                    .ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(m => m.Id == movementId
                                          && m.PorterId == porterId
                                          && m.MovementType == "CheckOutRequest"
                                          && m.Timestamp == null);
            if (movement == null) return NotFound();

            if (movement.RejectedAt != null)
            {
                TempData["ErrorMessage"] = "This request has already been rejected.";
                return RedirectToAction(nameof(MyMovements));
            }

            movement.RejectedAt = DateTime.Now;
            movement.RejectionReason = reason;
            await _context.SaveChangesAsync();

            // --------------- NOTIFICATION TO WARD ADMIN ---------------
            try
            {
                int? targetAdminId = movement.RequestedByWardAdminId
                                     ?? movement.Admission?.CreatedByWardAdminId;

                if (targetAdminId.HasValue)
                {
                    string porterName = (await _context.Employees.FindAsync(porterId))?.FullName ?? "Porter";
                    string patientName = movement.Admission?.Patient?.FullName ?? "a patient";
                    string msg = $"{porterName} declined to move {patientName}. Reason: {reason}";

                    await _notifService.NotifyUserAsync(
                        targetAdminId.Value,
                        "Employee",
                        msg,
                        Url.Action("Details", "WardAdmin", new { id = movement.AdmissionId }));
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Movement request rejected.";
            return RedirectToAction(nameof(MyMovements));
        }

        // ==================================================================
        //  REASSIGN MOVEMENT – GET (choose new porter)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ReassignMovement(int movementId)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var movement = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(m => m.Id == movementId
                                          && m.PorterId == porterId
                                          && m.MovementType == "CheckOutRequest"
                                          && m.Timestamp == null);
            if (movement == null) return NotFound();

            // Get all active porters except the current one
            ViewBag.Porters = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.PORTER && e.IsActive == Status.Active && e.EmployeeID != porterId)
                    .OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName");

            ViewBag.PatientName = movement.Admission?.Patient?.FullName;
            return View(movement);
        }

        // ==================================================================
        //  REASSIGN MOVEMENT – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReassignMovement(int movementId, int newPorterId)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var movement = await _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(m => m.Id == movementId
                                          && m.PorterId == porterId
                                          && m.MovementType == "CheckOutRequest"
                                          && m.Timestamp == null);
            if (movement == null) return NotFound();

            var newPorter = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeID == newPorterId && e.Role == UserRole.PORTER && e.IsActive == Status.Active);
            if (newPorter == null)
            {
                TempData["ErrorMessage"] = "Selected porter is invalid.";
                return RedirectToAction(nameof(ReassignMovement), new { movementId });
            }

            // Reset acceptance and rejection flags so the new porter can accept it fresh
            movement.PorterId = newPorterId;
            movement.AcceptedAt = null;
            movement.RejectedAt = null;
            movement.RejectionReason = null;

            await _context.SaveChangesAsync();

            // Notify the new porter
            try
            {
                string oldPorterName = (await _context.Employees.FindAsync(porterId))?.FullName ?? "A porter";
                string patientName = movement.Admission?.Patient?.FullName ?? "a patient";
                await _notifService.NotifyUserAsync(
                    newPorterId,
                    "Employee",
                    $"{oldPorterName} has reassigned a patient movement to you: {patientName} to {movement.Location}.",
                    Url.Action("MyMovements", "Porter"));
            }
            catch (Exception ex) { Console.WriteLine("Notify new porter error: " + ex.Message); }

            TempData["SuccessMessage"] = $"Movement reassigned to {newPorter.FullName}.";
            return RedirectToAction(nameof(MyMovements));
        }

        // ==================================================================
        //  COMPLETED MOVEMENTS LIST (with date filters)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> CompletedMovements(DateTime? startDate, DateTime? endDate)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var query = _context.PatientMovements
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .Where(m => m.PorterId == porterId &&
                            (m.MovementType == "CheckOut" || m.MovementType == "CheckIn") &&
                            m.Timestamp.HasValue);

            if (startDate.HasValue)
                query = query.Where(m => m.Timestamp!.Value.Date >= startDate.Value.Date);

            if (endDate.HasValue)
                query = query.Where(m => m.Timestamp!.Value.Date <= endDate.Value.Date);

            var movements = await query
                .OrderByDescending(m => m.Timestamp)
                .ToListAsync();

            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");

            return View(movements);
        }

        // ==================================================================
        //  SET CURRENT LOCATION / ZONE – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> SetLocation()
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            var porter = await _context.Employees.FindAsync(porterId.Value);
            if (porter == null) return NotFound();

            var locations = await _context.HospitalLocations
                .Where(l => l.IsActive == Status.Active)
                .OrderBy(l => l.Name)
                .ToListAsync();

            ViewBag.Locations = new SelectList(locations, "Name", "Name", porter.CurrentZone);

            return View(porter);
        }

        // ==================================================================
        //  SET CURRENT LOCATION / ZONE – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetLocation(string zone)
        {
            int? porterId = GetCurrentPorterId();
            if (porterId == null) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(zone))
            {
                TempData["ErrorMessage"] = "Please enter or select a zone.";
                return RedirectToAction(nameof(SetLocation));
            }

            var porter = await _context.Employees.FindAsync(porterId.Value);
            if (porter == null) return NotFound();

            porter.CurrentZone = zone.Trim();
            porter.CurrentZoneUpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Your current zone has been updated to '{porter.CurrentZone}'.";
            return RedirectToAction(nameof(Dashboard));
        }
    }
}