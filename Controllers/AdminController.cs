using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    public class AdminController : Controller
    {
        private readonly WardDbContext _context;

        public AdminController(WardDbContext context)
        {
            _context = context;
        }

        // ---------------------------------------------------------------
        //  DASHBOARD
        // ---------------------------------------------------------------
        public IActionResult Dashboard()
        {
            return View();
        }

        // ===============================================================
        //  EMPLOYEES – CRUD + SOFT DELETE
        // ===============================================================

        // LIST (optional filter by role or status via query string)
        public async Task<IActionResult> Employees(UserRole? role, Status? status)
        {
            var query = _context.Employees.AsQueryable();

            if (role.HasValue)
                query = query.Where(e => e.Role == role.Value);

            if (status.HasValue)
                query = query.Where(e => e.IsActive == status.Value);

            var employees = await query
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync();

            ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>(), role);
            ViewBag.Statuses = new SelectList(Enum.GetValues<Status>(), status);
            return View(employees);
        }

        // CREATE – GET
        public IActionResult CreateEmployee()
        {
            ViewBag.Roles = new SelectList(Enum.GetValues<UserRole>());
            ViewBag.Genders = new SelectList(Enum.GetValues<GenderType>());
            return View();
        }

        // CREATE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEmployee(Employee employee)
        {
            // Remove navigation / non‑editable properties from validation
            ModelState.Remove("EmployeeID");
            ModelState.Remove("FullName");
            ModelState.Remove("IsActive");        // set manually
            ModelState.Remove("PasswordHash");    // not set here
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

            employee.IsActive = Status.Active;
            employee.FailedLoginAttempts = 0;

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Employee created successfully.";
            return RedirectToAction(nameof(Employees));
        }

        // EDIT – GET
        public async Task<IActionResult> EditEmployee(int id)
        {
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

            // Keep these fields untouched
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
        public async Task<IActionResult> DetailsEmployee(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();
            return View(employee);
        }

        // SOFT DELETE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            employee.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Employee deactivated (soft deleted).";
            return RedirectToAction(nameof(Employees));
        }

        // RESTORE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreEmployee(int id)
        {
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
        public async Task<IActionResult> Wards()
        {
            var wards = await _context.Wards
                .OrderBy(w => w.Name)
                .ToListAsync();
            return View(wards);
        }

        // CREATE – GET
        public IActionResult CreateWard()
        {
            return View();
        }

        // CREATE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateWard(Ward ward)
        {
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
        public async Task<IActionResult> EditWard(int id)
        {
            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();
            return View(ward);
        }

        // EDIT – POST
        [HttpPost]
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
        public async Task<IActionResult> DetailsWard(int id)
        {
            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();
            return View(ward);
        }

        // SOFT DELETE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWard(int id)
        {
            var ward = await _context.Wards.FindAsync(id);
            if (ward == null) return NotFound();

            ward.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Ward deactivated (soft deleted).";
            return RedirectToAction(nameof(Wards));
        }

        // RESTORE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreWard(int id)
        {
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
        public async Task<IActionResult> Beds(int? wardId)
        {
            var query = _context.Beds.AsQueryable();
            if (wardId.HasValue)
                query = query.Where(b => b.WardId == wardId.Value);

            var beds = await query
                .Include(b => b.Ward)
                .OrderBy(b => b.Ward.Name)
                .ThenBy(b => b.BedNumber)
                .ToListAsync();

            ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name", wardId);
            return View(beds);
        }

        // CREATE – GET
        public IActionResult CreateBed()
        {
            ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name");
            return View();
        }

        // CREATE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBed(Bed bed)
        {
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
        public async Task<IActionResult> EditBed(int id)
        {
            var bed = await _context.Beds.FindAsync(id);
            if (bed == null) return NotFound();

            ViewBag.Wards = new SelectList(_context.Wards.Where(w => w.IsActive == Status.Active), "Id", "Name", bed.WardId);
            return View(bed);
        }

        // EDIT – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBed(int id, Bed bed)
        {
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
        public async Task<IActionResult> DetailsBed(int id)
        {
            var bed = await _context.Beds
                .Include(b => b.Ward)
                .FirstOrDefaultAsync(b => b.Id == id);
            if (bed == null) return NotFound();
            return View(bed);
        }

        // SOFT DELETE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBed(int id)
        {
            var bed = await _context.Beds.FindAsync(id);
            if (bed == null) return NotFound();

            bed.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Bed deactivated (soft deleted).";
            return RedirectToAction(nameof(Beds));
        }

        // RESTORE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreBed(int id)
        {
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
        public async Task<IActionResult> Consumables()
        {
            var consumables = await _context.Consumables
                .OrderBy(c => c.Name)
                .ToListAsync();
            return View(consumables);
        }

        // CREATE – GET
        public IActionResult CreateConsumable()
        {
            return View();
        }

        // CREATE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateConsumable(Consumable consumable)
        {
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
        public async Task<IActionResult> EditConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        // EDIT – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditConsumable(int id, Consumable posted)
        {
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
        public async Task<IActionResult> DetailsConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        // SOFT DELETE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();

            consumable.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable deactivated (soft deleted).";
            return RedirectToAction(nameof(Consumables));
        }

        // RESTORE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreConsumable(int id)
        {
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
        public async Task<IActionResult> Medications()
        {
            var medications = await _context.Medications
                .OrderBy(m => m.Name)
                .ToListAsync();
            return View(medications);
        }

        // CREATE – GET
        public IActionResult CreateMedication()
        {
            return View();
        }

        // CREATE – POST
        [HttpPost]
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
        public async Task<IActionResult> EditMedication(int id)
        {
            var medication = await _context.Medications.FindAsync(id);
            if (medication == null) return NotFound();
            return View(medication);
        }

        // EDIT – POST
        [HttpPost]
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
        public async Task<IActionResult> DetailsMedication(int id)
        {
            var medication = await _context.Medications.FindAsync(id);
            if (medication == null) return NotFound();
            return View(medication);
        }

        // SOFT DELETE – POST
        [HttpPost]
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
        [HttpPost]
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

        public async Task<IActionResult> Allergies()
        {
            var allergies = await _context.Allergies
                .OrderBy(a => a.Name)
                .ToListAsync();
            return View(allergies);
        }

        public IActionResult CreateAllergy() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAllergy(Allergy allergy)
        {
            ModelState.Remove("Id");
            if (!ModelState.IsValid) return View(allergy);

            allergy.IsActive = Status.Active;
            _context.Allergies.Add(allergy);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Allergy created.";
            return RedirectToAction(nameof(Allergies));
        }

        public async Task<IActionResult> EditAllergy(int id)
        {
            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();
            return View(allergy);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAllergy(int id, Allergy posted)
        {
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

        public async Task<IActionResult> DetailsAllergy(int id)
        {
            var allergy = await _context.Allergies.FindAsync(id);
            if (allergy == null) return NotFound();
            return View(allergy);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllergy(int id)
        {
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

        public async Task<IActionResult> Conditions()
        {
            var conditions = await _context.Conditions
                .OrderBy(c => c.Name)
                .ToListAsync();
            return View(conditions);
        }

        public IActionResult CreateCondition() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCondition(Condition condition)
        {
            ModelState.Remove("Id");
            if (!ModelState.IsValid) return View(condition);

            condition.IsActive = Status.Active;
            _context.Conditions.Add(condition);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Condition created.";
            return RedirectToAction(nameof(Conditions));
        }

        public async Task<IActionResult> EditCondition(int id)
        {
            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();
            return View(condition);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCondition(int id, Condition posted)
        {
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

        public async Task<IActionResult> DetailsCondition(int id)
        {
            var condition = await _context.Conditions.FindAsync(id);
            if (condition == null) return NotFound();
            return View(condition);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCondition(int id)
        {
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
        public async Task<IActionResult> HospitalInfo()
        {
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

        public async Task<IActionResult> EditHospitalInfo(int id)
        {
            var info = await _context.HospitalInfos.FindAsync(id);
            if (info == null) return NotFound();
            return View(info);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditHospitalInfo(int id, HospitalInfo posted)
        {
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
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHospitalInfo(int id)
        {
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
            var info = await _context.HospitalInfos.FindAsync(id);
            if (info == null) return NotFound();

            info.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Hospital info reactivated.";
            return RedirectToAction(nameof(HospitalInfo));
        }
    }
}