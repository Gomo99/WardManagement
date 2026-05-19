using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "SocialWorker")]

    public class SocialWorkerController : Controller
    {
        private readonly WardDbContext _context;

        public SocialWorkerController(WardDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------
        //  HELPER – get current Social Worker's EmployeeID
        // ------------------------------------------------------------------
        private int? GetCurrentSocialWorkerId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.SOCIALWORKER.ToString())
                return null;
            return id;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ViewBag.ActiveAdmissions = await _context.Admissions
                .CountAsync(a => a.IsActive == Status.Active);
            ViewBag.MyPlans = await _context.DischargePlans
                .CountAsync(dp => dp.SocialWorkerId == swId && dp.IsActive == Status.Active);
            return View();
        }

        // ==================================================================
        //  LIST ALL ACTIVE ADMISSIONS (patients to plan for)
        // ==================================================================
        public async Task<IActionResult> Index()
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => a.IsActive == Status.Active)
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();

            return View(admissions);
        }

        // ==================================================================
        //  VIEW PLANS FOR A SPECIFIC ADMISSION
        // ==================================================================
        public async Task<IActionResult> PlansByAdmission(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            var plans = await _context.DischargePlans
                .Include(dp => dp.SocialWorker)
                .Where(dp => dp.AdmissionId == admissionId && dp.IsActive == Status.Active)
                .OrderByDescending(dp => dp.CreatedAt)
                .ToListAsync();

            return View(plans);
        }

        // ==================================================================
        //  CREATE DISCHARGE PLAN – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> Create(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            return View(new DischargePlan
            {
                AdmissionId = admissionId,
                SocialWorkerId = swId.Value,
                CreatedAt = DateTime.Now
            });
        }

        // ==================================================================
        //  CREATE DISCHARGE PLAN – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DischargePlan plan)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == plan.AdmissionId);
                if (admission != null)
                    ViewBag.PatientName = admission.Patient.FullName;
                ViewBag.AdmissionId = plan.AdmissionId;
                return View(plan);
            }

            plan.SocialWorkerId = swId.Value;
            plan.IsActive = Status.Active;
            _context.DischargePlans.Add(plan);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Discharge plan created.";
            return RedirectToAction(nameof(PlansByAdmission), new { admissionId = plan.AdmissionId });
        }

        // ==================================================================
        //  EDIT DISCHARGE PLAN – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var plan = await _context.DischargePlans
                .Include(dp => dp.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(dp => dp.Id == id && dp.IsActive == Status.Active);
            if (plan == null) return NotFound();

            ViewBag.PatientName = plan.Admission.Patient.FullName;
            return View(plan);
        }

        // ==================================================================
        //  EDIT DISCHARGE PLAN – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DischargePlan posted)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var plan = await _context.DischargePlans
                    .Include(dp => dp.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(dp => dp.Id == id);
                if (plan != null)
                    ViewBag.PatientName = plan.Admission.Patient.FullName;
                return View(posted);
            }

            var existing = await _context.DischargePlans.FindAsync(id);
            if (existing == null || existing.IsActive != Status.Active) return NotFound();

            existing.PlanDetails = posted.PlanDetails;
            // keep original social worker and admission
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Discharge plan updated.";
            return RedirectToAction(nameof(PlansByAdmission), new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  DETAILS
        // ==================================================================
        public async Task<IActionResult> Details(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var plan = await _context.DischargePlans
                .Include(dp => dp.Admission).ThenInclude(a => a.Patient)
                .Include(dp => dp.SocialWorker)
                .FirstOrDefaultAsync(dp => dp.Id == id && dp.IsActive == Status.Active);
            if (plan == null) return NotFound();

            return View(plan);
        }

        // ==================================================================
        //  SOFT DELETE
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var plan = await _context.DischargePlans.FindAsync(id);
            if (plan == null) return NotFound();

            plan.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Discharge plan deactivated.";
            return RedirectToAction(nameof(PlansByAdmission), new { admissionId = plan.AdmissionId });
        }

        // ==================================================================
        //  RESTORE
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var plan = await _context.DischargePlans.FindAsync(id);
            if (plan == null) return NotFound();

            plan.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Discharge plan restored.";
            return RedirectToAction(nameof(PlansByAdmission), new { admissionId = plan.AdmissionId });
        }
    }
}