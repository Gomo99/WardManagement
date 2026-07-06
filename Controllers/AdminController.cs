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

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "ADMINISTRATOR")]
    [Route("[controller]")]

    public class AdminController : Controller
    {
        private readonly WardDbContext _context;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notifService;     // <-- new

        public AdminController(WardDbContext context,
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

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");


            ViewBag.TotalEmployees = await _context.Employees.CountAsync();
            ViewBag.ActiveWards = await _context.Wards.CountAsync(w => w.IsActive == Status.Active);
            ViewBag.TotalBeds = await _context.Beds.CountAsync();
            ViewBag.MedicationsCount = await _context.Medications.CountAsync();
            return View();
        }

        // ===============================================================
        //  EMPLOYEES – CRUD + SOFT DELETE
        // ===============================================================

        // LIST (optional filter by role or status via query string)
        // ===============================================================
        //  EMPLOYEES – CRUD + SOFT DELETE
        // ===============================================================

        // LIST (defaults to Active employees)
        [HttpGet("Employees")]
        public async Task<IActionResult> Employees(UserRole? role, string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");


            var query = _context.Employees.AsQueryable();

            if (role.HasValue)
                query = query.Where(e => e.Role == role.Value);

            // Filter by status – default to Active
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
            {
                query = query.Where(e => e.IsActive == parsedStatus);
            }
            // If status is "All" or any non‑parseable value, no filter is applied → shows all.

            var employees = await query
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync();

            ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>(), role);

            // Build a SelectList with string keys so "All" can be passed
            var statusOptions = new List<SelectListItem>
    {
        new SelectListItem { Text = "Active", Value = "Active", Selected = (status == "Active") },
        new SelectListItem { Text = "Inactive", Value = "Inactive", Selected = (status == "Inactive") },
        new SelectListItem { Text = "All", Value = "All", Selected = (status == "All") }
    };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(employees);
        }
        // CREATE – GET
        [HttpGet("CreateEmployee")]
        public IActionResult CreateEmployee()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");


            ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>());
            ViewBag.Genders = new SelectList(Enum.GetValues<GenderType>());
            return View();
        }

        // CREATE – POST (with auto-generated password)
        [HttpPost("CreateEmployee")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEmployee(Employee employee)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");


            ModelState.Remove("EmployeeID");
            ModelState.Remove("FullName");
            ModelState.Remove("IsActive");
            ModelState.Remove("PasswordHash");
            ModelState.Remove("EmailVerificationTokenHash");
            ModelState.Remove("EmailVerificationTokenExpires");
            ModelState.Remove("IsTwoFactorEnabled");
            ModelState.Remove("TwoFactorSecretKey");
            ModelState.Remove("TwoFactorRecoveryCodes");
            ModelState.Remove("ResetPin");
            ModelState.Remove("ResetPinExpiration");
            ModelState.Remove("FailedLoginAttempts");
            ModelState.Remove("LockoutEnd");
            ModelState.Remove("IsLockedOut");
            ModelState.Remove("MustChangePassword");
            ModelState.Remove("ResetToken");
            ModelState.Remove("ResetTokenExpiry");

            if (!ModelState.IsValid)
            {
                ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>(), employee.Role);
                ViewBag.Genders = new SelectList(Enum.GetValues<GenderType>(), employee.Gender);
                return View(employee);
            }

            // 1. Generate temporary password
            string tempPassword = GenerateRandomPassword(12);
            employee.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            employee.IsActive = Status.Active;
            employee.MustChangePassword = true;
            employee.FailedLoginAttempts = 0;

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();   // Employee now has an ID

            // 2. Email the temporary password
            // In AdminController.CreateEmployee POST method, replace the email sending section:

            try
            {
                string loginUrl = Url.Action("Login", "Account", null, Request.Scheme)!;
                await _emailService.SendEmployeeWelcomeEmailAsync(
                    employee.Email,
                    employee.FirstName,
                    employee.LastName,
                    employee.Email,
                    tempPassword,
                    loginUrl);

                TempData["SuccessMessage"] = $"Employee created. Temporary password emailed to {employee.Email}.";
            }
            catch (Exception ex)
            {
                TempData["SuccessMessage"] = $"Employee created, but email delivery failed ({ex.Message}).";
            }

            // 3. Send in-app notification to the new employee
            try
            {
                // Get the admin who is currently logged in
                var adminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                string adminName = "System";
                if (!string.IsNullOrEmpty(adminIdClaim) && int.TryParse(adminIdClaim, out int adminId))
                {
                    var admin = await _context.Employees.FindAsync(adminId);
                    if (admin != null)
                        adminName = $"{admin.FirstName} {admin.LastName}";
                }

                string notificationMsg = $"Your account was created by {adminName}. Please log in to change your password.";
                await _notifService.NotifyUserAsync(employee.EmployeeID, "Employee",
                    notificationMsg,
                    Url.Action("Login", "Account", null, Request.Scheme));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Notification error: " + ex.Message);
            }

            return RedirectToAction(nameof(Employees));
        }
        // EDIT – GET
        [HttpGet("EditEmployee")]

        public async Task<IActionResult> EditEmployee(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>(), employee.Role);
            ViewBag.Genders = new SelectList(Enum.GetValues<GenderType>(), employee.Gender);
            return View(employee);
        }

        // EDIT – POST (password is never edited)
        [HttpPost("EditEmployee")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEmployee(int id, Employee posted)
        {
            if (id != posted.EmployeeID) return BadRequest();

            // Keep password and security fields untouched
            ModelState.Remove("PasswordHash");
            ModelState.Remove("EmailVerificationTokenHash");
            ModelState.Remove("EmailVerificationTokenExpires");
            ModelState.Remove("IsTwoFactorEnabled");
            ModelState.Remove("TwoFactorSecretKey");
            ModelState.Remove("TwoFactorRecoveryCodes");
            ModelState.Remove("ResetPin");
            ModelState.Remove("ResetPinExpiration");
            ModelState.Remove("FailedLoginAttempts");
            ModelState.Remove("LockoutEnd");
            ModelState.Remove("IsLockedOut");
            ModelState.Remove("MustChangePassword");
            ModelState.Remove("ResetToken");
            ModelState.Remove("ResetTokenExpiry");
            ModelState.Remove("FullName");

            if (!ModelState.IsValid)
            {
                ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>(), posted.Role);
                ViewBag.Genders = new SelectList(Enum.GetValues<GenderType>(), posted.Gender);
                return View(posted);
            }

            try
            {
                var employee = await _context.Employees.FindAsync(id);
                if (employee == null) return NotFound();

                employee.FirstName = posted.FirstName;
                employee.LastName = posted.LastName;
                employee.UserName = posted.UserName;
                employee.Email = posted.Email;
                employee.Gender = posted.Gender;
                employee.Role = posted.Role;
                employee.HireDate = posted.HireDate;

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Employee updated.";
                return RedirectToAction(nameof(Employees));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Employees.Any(e => e.EmployeeID == id))
                    return NotFound();
                throw;
            }
        }

        // DETAILS, DELETE, RESTORE are unchanged (but listed below for completeness)
        [HttpGet("DetailsEmployee/{int:id")]
        public async Task<IActionResult> DetailsEmployee(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();
            return View(employee);
        }

        [HttpPost("DeleteEmployee/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();
            employee.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Employee deactivated.";
            return RedirectToAction(nameof(Employees));
        }

        [HttpPost("RestoreEmployee/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreEmployee(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();
            employee.IsActive = Status.Active;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Employee reactivated.";
            return RedirectToAction(nameof(Employees));
        }

        // ===============================================================
        //  WARDS – CRUD + SOFT DELETE
        // ===============================================================

        // LIST all wards
        [HttpGet("Wards")]
        public async Task<IActionResult> Wards(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Wards.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
            {
                query = query.Where(w => w.IsActive == parsedStatus);
            }
            // If status is "All" or invalid, no filter → shows all wards.

            var wards = await query.OrderBy(w => w.Name).ToListAsync();

            // Build a string‑based SelectList for the dropdown
            var statusOptions = new List<SelectListItem>
    {
        new SelectListItem("Active", "Active", status == "Active"),
        new SelectListItem("Inactive", "Inactive", status == "Inactive"),
        new SelectListItem("All", "All", status == "All")
    };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(wards);
        }

        // CREATE – GET
        [HttpGet("CreateWard")]
        public IActionResult CreateWard()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            return View();
        }

        // CREATE – POST
        [HttpPost("CreateWard")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateWard(Ward ward)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Beds");

            if (!ModelState.IsValid)
                return View(ward);

            ward.IsActive = Status.Active;
            _context.Wards.Add(ward);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Ward created successfully.";
            return RedirectToAction(nameof(Wards));
        }

        // EDIT – GET
        [HttpGet("EditWard/{int:id}")]
        public async Task<IActionResult> EditWard(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();
            return View(ward);
        }

        // EDIT – POST
        [HttpPost("EditWard")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditWard(int id, Ward ward)
        {
            if (id != ward.Id) return BadRequest();

            ModelState.Remove("Beds");
            if (!ModelState.IsValid)
                return View(ward);

            try
            {
                var existing = await _context.Wards.FindAsync(id);
                if (existing == null) return NotFound();

                existing.Name = ward.Name;
                existing.Description = ward.Description;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Ward updated.";
                return RedirectToAction(nameof(Wards));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Wards.Any(w => w.Id == id))
                    return NotFound();
                throw;
            }
        }

        // DETAILS
        [HttpGet("DetailsWard/{int:id}")]
        public async Task<IActionResult> DetailsWard(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();
            return View(ward);
        }

        // SOFT DELETE – POST
        [HttpPost("DeleteWard /{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWard(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();

            ward.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Ward deactivated (soft deleted).";
            return RedirectToAction(nameof(Wards));
        }

        // RESTORE – POST
        [HttpPost("RestoreWard /{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreWard(int id)
        {

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();

            ward.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Ward reactivated.";
            return RedirectToAction(nameof(Wards));
        }

        // ===============================================================
        //  BEDS – CRUD + SOFT DELETE
        // ===============================================================

        // LIST all beds (optionally filter by ward using query string ?wardId=)
        [HttpGet("Beds")]
        public async Task<IActionResult> Beds(int? wardId, string status = "Active")
        {

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Beds.AsQueryable();

            // Filter by ward
            if (wardId.HasValue)
                query = query.Where(b => b.WardId == wardId.Value);

            // Filter by status – default to Active
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
            {
                query = query.Where(b => b.IsActive == parsedStatus);
            }
            // If status is "All" or any invalid value, no status filter is applied → shows all beds.

            var beds = await query
                .Include(b => b.Ward)
                .OrderBy(b => b.Ward.Name)
                .ThenBy(b => b.BedNumber)
                .ToListAsync();

            // Ward dropdown (only active wards)
            ViewBag.Wards = new SelectList(
                _context.Wards.Where(w => w.IsActive == Status.Active),
                "Id", "Name", wardId);

            // Status dropdown
            var statusOptions = new List<SelectListItem>
    {
        new SelectListItem("Active", "Active", status == "Active"),
        new SelectListItem("Inactive", "Inactive", status == "Inactive"),
        new SelectListItem("All", "All", status == "All")
    };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(beds);
        }

        // CREATE – GET
        [HttpGet("CreateBed")]
        public IActionResult CreateBed()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name");
            return View();
        }

        // CREATE – POST
        [HttpPost("CreateBed")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBed(Bed bed)
        {

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Ward");

            if (!ModelState.IsValid)
            {
                ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name", bed.WardId);
                return View(bed);
            }

            bed.IsActive = Status.Active;
            _context.Beds.Add(bed);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Bed created successfully.";
            return RedirectToAction(nameof(Beds));
        }

        // EDIT – GET
        [HttpGet("EditBed/{int:id}")]
        public async Task<IActionResult> EditBed(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var bed = await _context.Beds.FindAsync(id);
            if (bed == null) return NotFound();

            ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name", bed.WardId);
            return View(bed);
        }

        // EDIT – POST
        [HttpPost("EditBed")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBed(int id, Bed bed)
        {

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != bed.Id) return BadRequest();

            ModelState.Remove("Ward");
            if (!ModelState.IsValid)
            {
                ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name", bed.WardId);
                return View(bed);
            }

            try
            {
                var existing = await _context.Beds.FindAsync(id);
                if (existing == null) return NotFound();

                existing.BedNumber = bed.BedNumber;
                existing.WardId = bed.WardId;
                existing.IsOccupied = bed.IsOccupied;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Bed updated.";
                return RedirectToAction(nameof(Beds));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Beds.Any(b => b.Id == id))
                    return NotFound();
                throw;
            }
        }

        // DETAILS
        [HttpGet("DetailsBed/{int:id}")]
        public async Task<IActionResult> DetailsBed(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var bed = await _context.Beds
                .Include(b => b.Ward)
                .FirstOrDefaultAsync(b => b.Id == id);
            if (bed == null) return NotFound();
            return View(bed);
        }

        // SOFT DELETE – POST
        [HttpPost("DeleteBed/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBed(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var bed = await _context.Beds.FindAsync(id);
            if (bed == null) return NotFound();

            bed.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Bed deactivated (soft deleted).";
            return RedirectToAction(nameof(Beds));
        }

        // RESTORE – POST
        [HttpPost("RestoreBed/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreBed(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var bed = await _context.Beds.FindAsync(id);
            if (bed == null) return NotFound();

            bed.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Bed reactivated.";
            return RedirectToAction(nameof(Beds));
        }

        // ===============================================================
        //  CONSUMABLES – CRUD + SOFT DELETE
        // ===============================================================

        // LIST all consumables
        [HttpGet("Consumables")]
        public async Task<IActionResult> Consumables(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Consumables.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
            {
                query = query.Where(c => c.IsActive == parsedStatus);
            }
            // If status is "All" or invalid, no filter → shows all consumables.

            var consumables = await query.OrderBy(c => c.Name).ToListAsync();

            var statusOptions = new List<SelectListItem>
    {
        new SelectListItem("Active", "Active", status == "Active"),
        new SelectListItem("Inactive", "Inactive", status == "Inactive"),
        new SelectListItem("All", "All", status == "All")
    };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(consumables);
        }
        // CREATE – GET
        [HttpGet("CreateConsumable")]
        public IActionResult CreateConsumable()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            return View();
        }

        // CREATE – POST
        [HttpPost("CreateConsumable")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateConsumable(Consumable consumable)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");

            if (!ModelState.IsValid)
                return View(consumable);

            consumable.IsActive = Status.Active;
            _context.Consumables.Add(consumable);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable created successfully.";
            return RedirectToAction(nameof(Consumables));
        }

        // EDIT – GET
        [HttpGet("EditConsumable/{int:id}")]
        public async Task<IActionResult> EditConsumable(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        // EDIT – POST
        [HttpPost("EditConsumable/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditConsumable(int id, Consumable posted)
        {

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            if (!ModelState.IsValid)
                return View(posted);

            try
            {
                var consumable = await _context.Consumables.FindAsync(id);
                if (consumable == null) return NotFound();

                consumable.Name = posted.Name;
                consumable.Description = posted.Description;
                consumable.QuantityOnHand = posted.QuantityOnHand;
                consumable.ReorderLevel = posted.ReorderLevel;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Consumable updated.";
                return RedirectToAction(nameof(Consumables));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Consumables.Any(c => c.Id == id))
                    return NotFound();
                throw;
            }
        }

        // DETAILS
        [HttpGet("DetailsConsumable/{int:id}")]

        public async Task<IActionResult> DetailsConsumable(int id)
        {

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        // SOFT DELETE – POST
        [HttpPost("DeleteConsumable/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConsumable(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();

            consumable.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable deactivated (soft deleted).";
            return RedirectToAction(nameof(Consumables));
        }

        // RESTORE – POST
        [HttpPost("RestoreConsumable/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreConsumable(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();

            consumable.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable reactivated.";
            return RedirectToAction(nameof(Consumables));
        }

        // ===============================================================
        //  MEDICATIONS – CRUD + SOFT DELETE
        // ===============================================================

        // LIST all medications
        [HttpGet("Medications")]
        public async Task<IActionResult> Medications(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Medications.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
            {
                query = query.Where(m => m.IsActive == parsedStatus);
            }
            // If status is "All" or invalid, no filter → shows all medications.

            var medications = await query.OrderBy(m => m.Name).ToListAsync();

            var statusOptions = new List<SelectListItem>
    {
        new SelectListItem("Active", "Active", status == "Active"),
        new SelectListItem("Inactive", "Inactive", status == "Inactive"),
        new SelectListItem("All", "All", status == "All")
    };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(medications);
        }


        // CREATE – GET
        [HttpGet("CreateMedication")]
        public IActionResult CreateMedication()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            return View();
        }


        // CREATE – POST
        [HttpPost("CreateMedication")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMedication(Medication medication)
        {
            ModelState.Remove("Id");

            if (!ModelState.IsValid)
                return View(medication);

            medication.IsActive = Status.Active;
            _context.Medications.Add(medication);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication created successfully.";
            return RedirectToAction(nameof(Medications));
        }

        // EDIT – GET
        [HttpGet("EditMedication/{int:id}")]
        public async Task<IActionResult> EditMedication(int id)
        {
            var medication = await _context.Medications.FindAsync(id);
            if (medication == null) return NotFound();
            return View(medication);
        }

        // EDIT – POST
        [HttpPost("EditMedication/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMedication(int id, Medication posted)
        {
            if (id != posted.Id) return BadRequest();

            if (!ModelState.IsValid)
                return View(posted);

            try
            {
                var medication = await _context.Medications.FindAsync(id);
                if (medication == null) return NotFound();

                medication.Name = posted.Name;
                medication.Description = posted.Description;
                medication.DosageForm = posted.DosageForm;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Medication updated.";
                return RedirectToAction(nameof(Medications));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Medications.Any(m => m.Id == id))
                    return NotFound();
                throw;
            }
        }

        // DETAILS
        [HttpGet("DetailsMedication/{int:id}")]
        public async Task<IActionResult> DetailsMedication(int id)
        {
            var medication = await _context.Medications.FindAsync(id);
            if (medication == null) return NotFound();
            return View(medication);
        }

        // SOFT DELETE – POST
        [HttpPost("DeleteMedication/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMedication(int id)
        {
            var medication = await _context.Medications.FindAsync(id);
            if (medication == null) return NotFound();

            medication.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication deactivated (soft deleted).";
            return RedirectToAction(nameof(Medications));
        }

        // RESTORE – POST
        [HttpPost("RestoreMedication/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreMedication(int id)
        {
            var medication = await _context.Medications.FindAsync(id);
            if (medication == null) return NotFound();

            medication.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication reactivated.";
            return RedirectToAction(nameof(Medications));
        }

        // ===============================================================
        //  ALLERGIES – CRUD + SOFT DELETE
        // ===============================================================

        [HttpGet("Allergies")]
        public async Task<IActionResult> Allergies(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Allergies.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
            {
                query = query.Where(a => a.IsActive == parsedStatus);
            }
            // If status is "All" or invalid, no filter → shows all allergies.

            var allergies = await query.OrderBy(a => a.Name).ToListAsync();

            var statusOptions = new List<SelectListItem>
    {
        new SelectListItem("Active", "Active", status == "Active"),
        new SelectListItem("Inactive", "Inactive", status == "Inactive"),
        new SelectListItem("All", "All", status == "All")
    };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(allergies);
        }

        [HttpGet("CreateAllergy")]
        public IActionResult CreateAllergy()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            return View();
        }

        [HttpPost("CreateAllergy")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAllergy(Allergy allergy)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            if (!ModelState.IsValid) return View(allergy);

            allergy.IsActive = Status.Active;
            _context.Allergies.Add(allergy);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Allergy created.";
            return RedirectToAction(nameof(Allergies));
        }

        [HttpGet("EditAllergy/{int:id}")]
        public async Task<IActionResult> EditAllergy(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();
            return View(allergy);
        }

        [HttpPost("EditAllergy/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAllergy(int id, Allergy posted)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            if (!ModelState.IsValid) return View(posted);

            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();

            allergy.Name = posted.Name;
            allergy.Description = posted.Description;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Allergy updated.";
            return RedirectToAction(nameof(Allergies));
        }

        [HttpGet("DetailsAllergy/{int:id}")]
        public async Task<IActionResult> DetailsAllergy(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();
            return View(allergy);
        }

        [HttpPost("DeleteAllergy/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllergy(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();

            allergy.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Allergy deactivated.";
            return RedirectToAction(nameof(Allergies));
        }

        [HttpPost("RestoreAllergy/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreAllergy(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();

            allergy.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Allergy reactivated.";
            return RedirectToAction(nameof(Allergies));
        }

        // ===============================================================
        //  CONDITIONS – CRUD + SOFT DELETE
        // ===============================================================

        [HttpGet("Conditions")]
        public async Task<IActionResult> Conditions(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Conditions.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
            {
                query = query.Where(c => c.IsActive == parsedStatus);
            }
            // If status is "All" or invalid, no filter → shows all conditions.

            var conditions = await query.OrderBy(c => c.Name).ToListAsync();

            var statusOptions = new List<SelectListItem>
    {
        new SelectListItem("Active", "Active", status == "Active"),
        new SelectListItem("Inactive", "Inactive", status == "Inactive"),
        new SelectListItem("All", "All", status == "All")
    };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(conditions);
        }

        [HttpGet("CreateCondition")]
        public IActionResult CreateCondition()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

           return View();
        }

        [HttpPost("CreateCondition")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCondition(Condition condition)
        {

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");


            ModelState.Remove("Id");
            if (!ModelState.IsValid) return View(condition);

            condition.IsActive = Status.Active;
            _context.Conditions.Add(condition);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Condition created.";
            return RedirectToAction(nameof(Conditions));
        }

        [HttpGet("EditCondition/{int:id}")]
        public async Task<IActionResult> EditCondition(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();
            return View(condition);
        }

        [HttpPost("EditCondition/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCondition(int id, Condition posted)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            if (!ModelState.IsValid) return View(posted);

            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();

            condition.Name = posted.Name;
            condition.Description = posted.Description;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Condition updated.";
            return RedirectToAction(nameof(Conditions));
        }


        [HttpGet("DetailsCondition/{int:id}")]

        public async Task<IActionResult> DetailsCondition(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();
            return View(condition);
        }

        [HttpPost("DeleteCondition/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCondition(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();

            condition.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Condition deactivated.";
            return RedirectToAction(nameof(Conditions));
        }

        [HttpPost("RestoreCondition/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreCondition(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();

            condition.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Condition reactivated.";
            return RedirectToAction(nameof(Conditions));
        }

        // ===============================================================
        //  HOSPITAL / BUSINESS INFO – (Single Record Management)
        // ===============================================================

        [HttpGet("HospitalInfo")]
        public async Task<IActionResult> HospitalInfo()
        {

            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var info = await _context.HospitalInfos.FirstOrDefaultAsync();
            if (info == null)
            {
                // Create a default record if none exists
                info = new HospitalInfo { IsActive = Status.Active };
                _context.HospitalInfos.Add(info);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(EditHospitalInfo), new { id = info.Id });
            }
            return View(info);
        }


        [HttpGet("EditHospitalInfo/{int:id}")]

        public async Task<IActionResult> EditHospitalInfo(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var info = await _context.HospitalInfos.FindAsync(id);
            if (info == null) return NotFound();
            return View(info);
        }

        [HttpPost("EditHospitalInfo/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditHospitalInfo(int id, HospitalInfo posted)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            if (!ModelState.IsValid) return View(posted);

            var info = await _context.HospitalInfos.FindAsync(id);
            if (info == null) return NotFound();

            info.HospitalName = posted.HospitalName;
            info.Address = posted.Address;
            info.ContactNumber = posted.ContactNumber;
            info.Email = posted.Email;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Hospital info updated.";
            return RedirectToAction(nameof(HospitalInfo));
        }

        // Soft delete / restore for hospital info (if needed)
        [HttpPost("DeleteHospitalInfo/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHospitalInfo(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var info = await _context.HospitalInfos.FindAsync(id);
            if (info == null) return NotFound();

            info.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Hospital info deactivated.";
            return RedirectToAction(nameof(HospitalInfo));
        }

        [HttpPost("RestoreHospitalInfo/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreHospitalInfo(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var info = await _context.HospitalInfos.FindAsync(id);
            if (info == null) return NotFound();

            info.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Hospital info reactivated.";
            return RedirectToAction(nameof(HospitalInfo));
        }



        // ===============================================================
        //  HOSPITAL LOCATIONS – CRUD + SOFT DELETE
        // ===============================================================

        [HttpGet("Locations")]
        public async Task<IActionResult> Locations(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.HospitalLocations.AsQueryable();
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsed))
                query = query.Where(l => l.IsActive == parsed);

            var locations = await query.OrderBy(l => l.Name).ToListAsync();

            ViewBag.Statuses = new SelectList(new List<SelectListItem>
    {
        new SelectListItem("Active", "Active", status == "Active"),
        new SelectListItem("Inactive", "Inactive", status == "Inactive"),
        new SelectListItem("All", "All", status == "All")
    }, "Value", "Text", status);

            return View(locations);
        }
        [HttpGet("CreateLocation")]

        public IActionResult CreateLocation() 
        { 
            
            
            return View(); 
        
        
        }

        [HttpPost("CreateLocation")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateLocation(HospitalLocation location)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            if (!ModelState.IsValid) return View(location);
            location.IsActive = Status.Active;
            _context.HospitalLocations.Add(location);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Location created.";
            return RedirectToAction(nameof(Locations));
        }

        [HttpGet("EditLocation/{int:id}")]
        public async Task<IActionResult> EditLocation(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var location = await _context.HospitalLocations.FindAsync(id);
            if (location == null) return NotFound();
            return View(location);
        }

        [HttpPost("EditLocation/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditLocation(int id, HospitalLocation posted)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            if (!ModelState.IsValid) return View(posted);
            var location = await _context.HospitalLocations.FindAsync(id);
            if (location == null) return NotFound();
            location.Name = posted.Name;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Location updated.";
            return RedirectToAction(nameof(Locations));
        }

        [HttpPost("DeleteLocation/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLocation(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var location = await _context.HospitalLocations.FindAsync(id);
            if (location == null) return NotFound();
            location.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Location deactivated.";
            return RedirectToAction(nameof(Locations));
        }

        [HttpPost("RestoreLocation/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreLocation(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var location = await _context.HospitalLocations.FindAsync(id);
            if (location == null) return NotFound();
            location.IsActive = Status.Active;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Location reactivated.";
            return RedirectToAction(nameof(Locations));
        }


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
    }
}