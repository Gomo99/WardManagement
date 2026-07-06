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
    // [Route("[controller]")]   ← REMOVED
    public class AdminController : Controller
    {
        private readonly WardDbContext _context;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notifService;

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

        // LIST (defaults to Active employees)
        [HttpGet]
        public async Task<IActionResult> Employees(UserRole? role, string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Employees.AsQueryable();
            if (role.HasValue)
                query = query.Where(e => e.Role == role.Value);
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
                query = query.Where(e => e.IsActive == parsedStatus);

            var employees = await query
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync();

            ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>(), role);
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
        [HttpGet]
        public IActionResult CreateEmployee()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>());
            ViewBag.Genders = new SelectList(Enum.GetValues<GenderType>());
            return View();
        }

        // CREATE – POST
        [HttpPost]
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

            string tempPassword = GenerateRandomPassword(12);
            employee.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            employee.IsActive = Status.Active;
            employee.MustChangePassword = true;
            employee.FailedLoginAttempts = 0;

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

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

            try
            {
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
        [HttpGet]
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

        // EDIT – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEmployee(int id, Employee posted)
        {
            if (id != posted.EmployeeID) return BadRequest();

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

        // DETAILS
        [HttpGet]
        public async Task<IActionResult> DetailsEmployee(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();
            return View(employee);
        }

        // DELETE
        [HttpPost]
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

        // RESTORE
        [HttpPost]
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
        [HttpGet]
        public async Task<IActionResult> Wards(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Wards.AsQueryable();
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
                query = query.Where(w => w.IsActive == parsedStatus);

            var wards = await query.OrderBy(w => w.Name).ToListAsync();

            var statusOptions = new List<SelectListItem>
            {
                new SelectListItem("Active", "Active", status == "Active"),
                new SelectListItem("Inactive", "Inactive", status == "Inactive"),
                new SelectListItem("All", "All", status == "All")
            };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(wards);
        }

        [HttpGet]
        public IActionResult CreateWard()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateWard(Ward ward)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Beds");
            if (!ModelState.IsValid) return View(ward);

            ward.IsActive = Status.Active;
            _context.Wards.Add(ward);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Ward created successfully.";
            return RedirectToAction(nameof(Wards));
        }

        [HttpGet]
        public async Task<IActionResult> EditWard(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();
            return View(ward);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditWard(int id, Ward ward)
        {
            if (id != ward.Id) return BadRequest();
            ModelState.Remove("Beds");
            if (!ModelState.IsValid) return View(ward);

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

        [HttpGet]
        public async Task<IActionResult> DetailsWard(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();
            return View(ward);
        }

        [HttpPost]
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

        [HttpPost]
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
        [HttpGet]
        public async Task<IActionResult> Beds(int? wardId, string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Beds.AsQueryable();
            if (wardId.HasValue)
                query = query.Where(b => b.WardId == wardId.Value);
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
                query = query.Where(b => b.IsActive == parsedStatus);

            var beds = await query
                .Include(b => b.Ward)
                .OrderBy(b => b.Ward.Name)
                .ThenBy(b => b.BedNumber)
                .ToListAsync();

            ViewBag.Wards = new SelectList(
                _context.Wards.Where(w => w.IsActive == Status.Active),
                "Id", "Name", wardId);

            var statusOptions = new List<SelectListItem>
            {
                new SelectListItem("Active", "Active", status == "Active"),
                new SelectListItem("Inactive", "Inactive", status == "Inactive"),
                new SelectListItem("All", "All", status == "All")
            };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(beds);
        }

        [HttpGet]
        public IActionResult CreateBed()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name");
            return View();
        }

        [HttpPost]
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

        [HttpGet]
        public async Task<IActionResult> EditBed(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var bed = await _context.Beds.FindAsync(id);
            if (bed == null) return NotFound();

            ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name", bed.WardId);
            return View(bed);
        }

        [HttpPost]
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

        [HttpGet]
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

        [HttpPost]
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

        [HttpPost]
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
        [HttpGet]
        public async Task<IActionResult> Consumables(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Consumables.AsQueryable();
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
                query = query.Where(c => c.IsActive == parsedStatus);

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

        [HttpGet]
        public IActionResult CreateConsumable()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateConsumable(Consumable consumable)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            if (!ModelState.IsValid) return View(consumable);

            consumable.IsActive = Status.Active;
            _context.Consumables.Add(consumable);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable created successfully.";
            return RedirectToAction(nameof(Consumables));
        }

        [HttpGet]
        public async Task<IActionResult> EditConsumable(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditConsumable(int id, Consumable posted)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            if (!ModelState.IsValid) return View(posted);

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

        [HttpGet]
        public async Task<IActionResult> DetailsConsumable(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        [HttpPost]
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

        [HttpPost]
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
        //  MEDICATIONS – CRUD + SOFT DELETE (with Allergy/Condition links)
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> Medications(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Medications
                .Include(m => m.AllergyMedications)
                    .ThenInclude(am => am.Allergy)
                .Include(m => m.ConditionMedications)
                    .ThenInclude(cm => cm.Condition)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
                query = query.Where(m => m.IsActive == parsedStatus);

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

        [HttpGet]
        public IActionResult CreateMedication()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            PopulateMedicationDropDowns();
            ViewBag.SelectedAllergyIds = new List<int>();
            ViewBag.SelectedConditionIds = new List<int>();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMedication(Medication medication, int[]? allergyIds, int[]? conditionIds)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("AllergyMedications");
            ModelState.Remove("ConditionMedications");

            if (!ModelState.IsValid)
            {
                PopulateMedicationDropDowns();
                return View(medication);
            }

            medication.IsActive = Status.Active;
            medication.AllergyMedications = new List<AllergyMedication>();
            medication.ConditionMedications = new List<ConditionMedication>();

            if (allergyIds != null)
                foreach (var allergyId in allergyIds)
                    medication.AllergyMedications.Add(new AllergyMedication { AllergyId = allergyId });

            if (conditionIds != null)
                foreach (var conditionId in conditionIds)
                    medication.ConditionMedications.Add(new ConditionMedication { ConditionId = conditionId });

            _context.Medications.Add(medication);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication created successfully.";
            return RedirectToAction(nameof(Medications));
        }

        [HttpGet]
        public async Task<IActionResult> EditMedication(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var medication = await _context.Medications
                .Include(m => m.AllergyMedications)
                .Include(m => m.ConditionMedications)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (medication == null) return NotFound();

            PopulateMedicationDropDowns();
            ViewBag.SelectedAllergyIds = medication.AllergyMedications
                .Select(am => am.AllergyId).ToList();
            ViewBag.SelectedConditionIds = medication.ConditionMedications
                .Select(cm => cm.ConditionId).ToList();

            return View(medication);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMedication(int id, Medication posted, int[]? allergyIds, int[]? conditionIds)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            ModelState.Remove("AllergyMedications");
            ModelState.Remove("ConditionMedications");

            if (!ModelState.IsValid)
            {
                PopulateMedicationDropDowns();
                return View(posted);
            }

            try
            {
                var medication = await _context.Medications
                    .Include(m => m.AllergyMedications)
                    .Include(m => m.ConditionMedications)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (medication == null) return NotFound();

                medication.Name = posted.Name;
                medication.Description = posted.Description;
                medication.DosageForm = posted.DosageForm;
                medication.Schedule = posted.Schedule;

                medication.AllergyMedications.Clear();
                if (allergyIds != null)
                    foreach (var allergyId in allergyIds)
                        medication.AllergyMedications.Add(new AllergyMedication { AllergyId = allergyId });

                medication.ConditionMedications.Clear();
                if (conditionIds != null)
                    foreach (var conditionId in conditionIds)
                        medication.ConditionMedications.Add(new ConditionMedication { ConditionId = conditionId });

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

        [HttpGet]
        public async Task<IActionResult> DetailsMedication(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var medication = await _context.Medications
                .Include(m => m.AllergyMedications)
                    .ThenInclude(am => am.Allergy)
                .Include(m => m.ConditionMedications)
                    .ThenInclude(cm => cm.Condition)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (medication == null) return NotFound();
            return View(medication);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMedication(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var medication = await _context.Medications.FindAsync(id);
            if (medication == null) return NotFound();

            medication.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication deactivated (soft deleted).";
            return RedirectToAction(nameof(Medications));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreMedication(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var medication = await _context.Medications.FindAsync(id);
            if (medication == null) return NotFound();

            medication.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication reactivated.";
            return RedirectToAction(nameof(Medications));
        }

        private void PopulateMedicationDropDowns()
        {
            var allergies = _context.Allergies
                .Where(a => a.IsActive == Status.Active)
                .OrderBy(a => a.Name)
                .ToList();

            var conditions = _context.Conditions
                .Where(c => c.IsActive == Status.Active)
                .OrderBy(c => c.Name)
                .ToList();

            ViewBag.Allergies = new SelectList(allergies, "Id", "Name");
            ViewBag.Conditions = new SelectList(conditions, "Id", "Name");
        }

        // ===============================================================
        //  ALLERGIES – CRUD + SOFT DELETE
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> Allergies(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Allergies.AsQueryable();
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
                query = query.Where(a => a.IsActive == parsedStatus);

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

        [HttpGet]
        public IActionResult CreateAllergy()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");
            return View();
        }

        [HttpPost]
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

        [HttpGet]
        public async Task<IActionResult> EditAllergy(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();
            return View(allergy);
        }

        [HttpPost]
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

        [HttpGet]
        public async Task<IActionResult> DetailsAllergy(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();
            return View(allergy);
        }

        [HttpPost]
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

        [HttpPost]
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
        [HttpGet]
        public async Task<IActionResult> Conditions(string status = "Active")
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Conditions.AsQueryable();
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
                query = query.Where(c => c.IsActive == parsedStatus);

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

        [HttpGet]
        public IActionResult CreateCondition()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");
            return View();
        }

        [HttpPost]
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

        [HttpGet]
        public async Task<IActionResult> EditCondition(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();
            return View(condition);
        }

        [HttpPost]
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

        [HttpGet]
        public async Task<IActionResult> DetailsCondition(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();
            return View(condition);
        }

        [HttpPost]
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

        [HttpPost]
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
        //  HOSPITAL / BUSINESS INFO
        // ===============================================================
        [HttpGet]
        public async Task<IActionResult> HospitalInfo()
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var info = await _context.HospitalInfos.FirstOrDefaultAsync();
            if (info == null)
            {
                info = new HospitalInfo { IsActive = Status.Active };
                _context.HospitalInfos.Add(info);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(EditHospitalInfo), new { id = info.Id });
            }
            return View(info);
        }

        [HttpGet]
        public async Task<IActionResult> EditHospitalInfo(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var info = await _context.HospitalInfos.FindAsync(id);
            if (info == null) return NotFound();
            return View(info);
        }

        [HttpPost]
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

        [HttpPost]
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

        [HttpPost]
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
        [HttpGet]
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

        [HttpGet]
        public IActionResult CreateLocation()
        {
            return View();
        }

        [HttpPost]
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

        [HttpGet]
        public async Task<IActionResult> EditLocation(int id)
        {
            int? managerId = GetCurrentWardAdminId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var location = await _context.HospitalLocations.FindAsync(id);
            if (location == null) return NotFound();
            return View(location);
        }

        [HttpPost]
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

        [HttpPost]
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

        [HttpPost]
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