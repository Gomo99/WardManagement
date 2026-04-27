using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.ViewModel;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    public class PatientController : Controller
    {
        private readonly WardDbContext _context;

        public PatientController(WardDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------
        //  HELPER – get current logged‑in user ID and role
        // ------------------------------------------------------------------
        private int? GetCurrentUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            return id;
        }

        private string? GetCurrentUserRole()
        {
            return User.FindFirstValue(ClaimTypes.Role);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE DASHBOARD
        // ==================================================================
        public async Task<IActionResult> Dashboard()
        {
            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();

            if (role != UserRole.PATIENT.ToString() || userId == null)
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients.FindAsync(userId.Value);
            if (patient == null || patient.IsActive != Status.Active)
                return RedirectToAction("Login", "Account");

            // Show summary: number of admissions, etc.
            var admissions = await _context.Admissions
                .Where(a => a.PatientId == userId.Value)
                .OrderByDescending(a => a.AdmissionDate)
                .ToListAsync();

            ViewBag.ActiveAdmission = admissions.FirstOrDefault(a => a.IsActive == Status.Active);
            ViewBag.TotalAdmissions = admissions.Count;

            return View(patient);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – VIEW PROFILE
        // ==================================================================
        public async Task<IActionResult> MyProfile()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients.FindAsync(userId.Value);
            if (patient == null) return NotFound();
            return View(patient);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – EDIT PROFILE
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> EditMyProfile()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients.FindAsync(userId.Value);
            if (patient == null) return NotFound();

            return View(new EditPatientProfileViewModel
            {
                Id = patient.Id,
                FirstName = patient.FirstName,
                LastName = patient.LastName,
                CellphoneNumber = patient.CellphoneNumber,
                Email = patient.Email,
                HomeAddress = patient.HomeAddress
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMyProfile(EditPatientProfileViewModel model)
        {
            var userId = GetCurrentUserId();
            if (userId == null || userId != model.Id || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
                return View(model);

            var patient = await _context.Patients.FindAsync(model.Id);
            if (patient == null) return NotFound();

            patient.FirstName = model.FirstName;
            patient.LastName = model.LastName;
            patient.CellphoneNumber = model.CellphoneNumber;
            patient.Email = model.Email;
            patient.HomeAddress = model.HomeAddress;
            // Date of birth and ID number should not be changed by patient

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Profile updated.";
            return RedirectToAction(nameof(MyProfile));
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – MY ADMISSIONS
        // ==================================================================
        public async Task<IActionResult> MyAdmissions()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Where(a => a.PatientId == userId.Value)
                .OrderByDescending(a => a.AdmissionDate)
                .ToListAsync();

            return View(admissions);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – VIEW FOLDER (similar to doctor folder)
        // ==================================================================
        public async Task<IActionResult> MyPatientFolder(int admissionId)
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.PatientId == userId.Value);

            if (admission == null) return NotFound();

            ViewBag.Vitals = await _context.Vitals
                .Where(v => v.AdmissionId == admissionId && v.IsActive == Status.Active)
                .OrderByDescending(v => v.DateRecorded).ToListAsync();
            ViewBag.Treatments = await _context.Treatments
                .Where(t => t.AdmissionId == admissionId && t.IsActive == Status.Active)
                .OrderByDescending(t => t.TreatmentDate).ToListAsync();
            ViewBag.MedicationAdministrations = await _context.MedicationAdministrations
                .Include(ma => ma.Medication)
                .Where(ma => ma.AdmissionId == admissionId && ma.IsActive == Status.Active)
                .OrderByDescending(ma => ma.DateAdministered).ToListAsync();
            ViewBag.DoctorVisits = await _context.DoctorVisits
                .Include(dv => dv.Doctor)
                .Where(dv => dv.AdmissionId == admissionId && dv.IsActive == Status.Active)
                .OrderByDescending(dv => dv.VisitDate).ToListAsync();

            return View(admission);
        }

        // ==================================================================
        //  PATIENT SELF‑SERVICE – DEACTIVATE ACCOUNT (soft delete)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateMyAccount()
        {
            var userId = GetCurrentUserId();
            if (userId == null || GetCurrentUserRole() != UserRole.PATIENT.ToString())
                return RedirectToAction("Login", "Account");

            var patient = await _context.Patients.FindAsync(userId.Value);
            if (patient == null) return NotFound();

            patient.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            // Log out
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);

            TempData["SuccessMessage"] = "Your account has been deactivated.";
            return RedirectToAction("Login", "Account");
        }

        
      

    }
}