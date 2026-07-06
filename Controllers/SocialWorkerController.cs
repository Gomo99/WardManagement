using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Components;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;
using WARDMANAGEMENTSYSTEM.ViewModel;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "SOCIALWORKER")]
    [Route("[controller]")]

    public class SocialWorkerController : Controller
    {
        private readonly WardDbContext _context;

        private readonly INotificationService _notifService;   // <-- add

        public SocialWorkerController(WardDbContext context, INotificationService notifService)
        {
            _context = context;
            _notifService = notifService;   // may be used later for reminders
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

            ViewBag.UpcomingFollowUps = await _context.FollowUps
    .CountAsync(f => f.SocialWorkerId == swId && f.IsActive && f.Status == FollowUpStatus.Pending
                     && f.ScheduledDate > DateTime.Now);
        }

        // ==================================================================
        //  LIST ALL ACTIVE ADMISSIONS (patients to plan for)
        // ==================================================================

        [HttpGet("Index")]
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

        [HttpGet("PlansByAdmission/{int:id}")]
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
        [HttpGet("Create/{int:id}")]
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
        [HttpPost("Create")]
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
            plan.DischargeStatus = DischargePlanStatus.Pending;
            plan.IsActive = Status.Active;
            _context.DischargePlans.Add(plan);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Discharge plan created.";
            return RedirectToAction(nameof(PlansByAdmission), new { admissionId = plan.AdmissionId });
        }

        // ==================================================================
        //  EDIT DISCHARGE PLAN – GET
        // ==================================================================
        [HttpGet("Edit/{int:id}")]
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
        [HttpPost("Edit/{int:id}")]
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

        [HttpGet("Details/{int:id}")]
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
        [HttpPost("Delete/{int:id}")]
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
        [HttpPost("Restore/{int:id}")]
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




        // ==================================================================
        //  PSYCHOSOCIAL ASSESSMENT – VIEW FOR ADMISSION
        // ==================================================================

        [HttpGet("AssessmentView/{int:id}")]
        public async Task<IActionResult> AssessmentView(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            var assessment = await _context.PsychosocialAssessments
                .FirstOrDefaultAsync(a => a.AdmissionId == admissionId && a.IsActive);

            // If none exists, redirect to create
            if (assessment == null)
                return RedirectToAction(nameof(AssessmentCreate), new { admissionId });

            return View(assessment);
        }

        // ==================================================================
        //  PSYCHOSOCIAL ASSESSMENT – CREATE (GET)
        // ==================================================================
        [HttpGet("AssessmentCreate/{int:id}")]
        public async Task<IActionResult> AssessmentCreate(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            // Check if an active assessment already exists
            var existing = await _context.PsychosocialAssessments
                .AnyAsync(a => a.AdmissionId == admissionId && a.IsActive);
            if (existing)
            {
                TempData["ErrorMessage"] = "An active assessment already exists for this patient.";
                return RedirectToAction(nameof(AssessmentView), new { admissionId });
            }

            ViewBag.PatientName = admission.Patient.FullName;
            return View(new PsychosocialAssessment { AdmissionId = admissionId, SocialWorkerId = swId.Value });
        }

        // ==================================================================
        //  PSYCHOSOCIAL ASSESSMENT – CREATE (POST)
        // ==================================================================
        [HttpPost("AssessmentCreate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssessmentCreate(PsychosocialAssessment assessment)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == assessment.AdmissionId);
                if (admission != null)
                    ViewBag.PatientName = admission.Patient.FullName;
                return View(assessment);
            }

            // Ensure the admission is active
            var validAdmission = await _context.Admissions
                .AnyAsync(a => a.Id == assessment.AdmissionId && a.IsActive == Status.Active);
            if (!validAdmission)
            {
                ModelState.AddModelError("", "Invalid or inactive admission.");
                return View(assessment);
            }

            // Ensure no duplicate active assessment
            var duplicate = await _context.PsychosocialAssessments
                .AnyAsync(a => a.AdmissionId == assessment.AdmissionId && a.IsActive);
            if (duplicate)
            {
                ModelState.AddModelError("", "An active assessment already exists for this patient.");
                return View(assessment);
            }

            assessment.SocialWorkerId = swId.Value;
            assessment.CreatedAt = DateTime.Now;
            assessment.IsActive = true;

            _context.PsychosocialAssessments.Add(assessment);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Psychosocial assessment saved.";
            return RedirectToAction(nameof(AssessmentView), new { admissionId = assessment.AdmissionId });
        }

        // ==================================================================
        //  PSYCHOSOCIAL ASSESSMENT – EDIT (GET)
        // ==================================================================
        [HttpGet("AssessmentEdit/{int:id}")]
        public async Task<IActionResult> AssessmentEdit(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var assessment = await _context.PsychosocialAssessments
                .Include(a => a.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == id && a.IsActive);

            if (assessment == null) return NotFound();

            ViewBag.PatientName = assessment.Admission.Patient.FullName;
            return View(assessment);
        }

        // ==================================================================
        //  PSYCHOSOCIAL ASSESSMENT – EDIT (POST)
        // ==================================================================
        [HttpPost("AssessmentEdit/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssessmentEdit(int id, PsychosocialAssessment posted)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var assessment = await _context.PsychosocialAssessments
                    .Include(a => a.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == id);
                if (assessment != null)
                    ViewBag.PatientName = assessment.Admission.Patient.FullName;
                return View(posted);
            }

            var existing = await _context.PsychosocialAssessments.FindAsync(id);
            if (existing == null || !existing.IsActive) return NotFound();

            existing.SocialHistory = posted.SocialHistory;
            existing.SupportNetwork = posted.SupportNetwork;
            existing.FinancialConcerns = posted.FinancialConcerns;
            existing.SubstanceUse = posted.SubstanceUse;
            existing.MentalHealthStatus = posted.MentalHealthStatus;
            existing.AdditionalNotes = posted.AdditionalNotes;
            existing.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Assessment updated.";
            return RedirectToAction(nameof(AssessmentView), new { admissionId = existing.AdmissionId });
        }




        // ==================================================================
        //  RISK SCREENINGS – LIST FOR ADMISSION
        // ==================================================================

        [HttpGet("RiskScreenings/{int:id}")]
        public async Task<IActionResult> RiskScreenings(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            var screenings = await _context.RiskScreenings
                .Include(r => r.SocialWorker)
                .Where(r => r.AdmissionId == admissionId && r.IsActive)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(screenings);
        }

        // ==================================================================
        //  RISK SCREENING – CREATE (GET)
        // ==================================================================
        [HttpGet("RiskScreeningCreate/{int:id}")]
        public async Task<IActionResult> RiskScreeningCreate(int admissionId, ScreeningType type)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.ScreeningType = type;

            return View(new RiskScreening
            {
                AdmissionId = admissionId,
                SocialWorkerId = swId.Value,
                Type = type
            });
        }

        // ==================================================================
        //  RISK SCREENING – CREATE (POST)
        // ==================================================================
        [HttpPost("RiskScreeningCreate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RiskScreeningCreate(RiskScreening screening)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == screening.AdmissionId);
                if (admission != null)
                    ViewBag.PatientName = admission.Patient.FullName;
                ViewBag.ScreeningType = screening.Type;
                return View(screening);
            }

            // Validate admission
            var validAdmission = await _context.Admissions
                .AnyAsync(a => a.Id == screening.AdmissionId && a.IsActive == Status.Active);
            if (!validAdmission)
            {
                ModelState.AddModelError("", "Invalid or inactive admission.");
                return View(screening);
            }

            screening.SocialWorkerId = swId.Value;
            screening.CreatedAt = DateTime.Now;
            screening.RiskLevel = CalculateRiskLevel(screening.Score);
            screening.IsActive = true;

            _context.RiskScreenings.Add(screening);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{screening.Type} screening saved. Risk level: {screening.RiskLevel}.";
            return RedirectToAction(nameof(RiskScreenings), new { admissionId = screening.AdmissionId });
        }

        // ==================================================================
        //  RISK SCREENING – VIEW DETAILS
        // ==================================================================

        [HttpGet("RiskScreeningDetails/{int:id}")]
        public async Task<IActionResult> RiskScreeningDetails(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var screening = await _context.RiskScreenings
                .Include(r => r.Admission).ThenInclude(a => a.Patient)
                .Include(r => r.SocialWorker)
                .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);
            if (screening == null) return NotFound();

            return View(screening);
        }

        // ==================================================================
        //  RISK SCREENING – EDIT (GET)
        // ==================================================================
        [HttpGet("RiskScreeningEdit/{int:id}")]
        public async Task<IActionResult> RiskScreeningEdit(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var screening = await _context.RiskScreenings
                .Include(r => r.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);
            if (screening == null) return NotFound();

            ViewBag.PatientName = screening.Admission.Patient.FullName;
            return View(screening);
        }

        // ==================================================================
        //  RISK SCREENING – EDIT (POST)
        // ==================================================================
        [HttpPost("RiskScreeningEdit/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RiskScreeningEdit(int id, RiskScreening posted)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var screening = await _context.RiskScreenings
                    .Include(r => r.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(r => r.Id == id);
                if (screening != null)
                    ViewBag.PatientName = screening.Admission.Patient.FullName;
                return View(posted);
            }

            var existing = await _context.RiskScreenings.FindAsync(id);
            if (existing == null || !existing.IsActive) return NotFound();

            existing.Score = posted.Score;
            existing.RiskLevel = CalculateRiskLevel(posted.Score);
            existing.RecommendedActions = posted.RecommendedActions;
            // Type is not editable

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Screening updated.";
            return RedirectToAction(nameof(RiskScreenings), new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  RISK SCREENING – SOFT DELETE
        // ==================================================================
        [HttpPost("RiskScreeningDelete/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RiskScreeningDelete(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var screening = await _context.RiskScreenings.FindAsync(id);
            if (screening == null) return NotFound();

            screening.IsActive = false;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Screening deactivated.";
            return RedirectToAction(nameof(RiskScreenings), new { admissionId = screening.AdmissionId });
        }




        // ==================================================================
        //  PATIENT NEEDS CHECKLIST – VIEW & MANAGE
        // ==================================================================
        [HttpGet("NeedsChecklist/{int:id}")]
        public async Task<IActionResult> NeedsChecklist(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            var needs = await _context.PatientNeeds
                .Where(n => n.AdmissionId == admissionId)
                .OrderBy(n => n.NeedName)
                .ToListAsync();

            // If no needs exist yet, create the default standard list
            if (needs.Count == 0)
            {
                var defaultNeeds = new List<string>
        {
            "Transport",
            "Home Care",
            "Equipment",
            "Meals",
            "Counselling"
        };

                foreach (var needName in defaultNeeds)
                {
                    _context.PatientNeeds.Add(new PatientNeed
                    {
                        AdmissionId = admissionId,
                        NeedName = needName,
                        IsCompleted = false
                    });
                }

                await _context.SaveChangesAsync();

                needs = await _context.PatientNeeds
                    .Where(n => n.AdmissionId == admissionId)
                    .OrderBy(n => n.NeedName)
                    .ToListAsync();
            }

            return View(needs);
        }

        // ==================================================================
        //  TOGGLE NEED COMPLETION
        // ==================================================================
        [HttpPost("ToggleNeed/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleNeed(int needId, int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var need = await _context.PatientNeeds
                .FirstOrDefaultAsync(n => n.Id == needId && n.AdmissionId == admissionId);
            if (need == null) return NotFound();

            need.IsCompleted = !need.IsCompleted;
            need.SocialWorkerId = swId;
            need.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"'{need.NeedName}' marked as {(need.IsCompleted ? "completed" : "incomplete")}.";
            return RedirectToAction(nameof(NeedsChecklist), new { admissionId });
        }

        // ==================================================================
        //  ADD CUSTOM NEED
        // ==================================================================
        [HttpPost("AddNeed/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNeed(int admissionId, string needName)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(needName))
            {
                TempData["ErrorMessage"] = "Please provide a need description.";
                return RedirectToAction(nameof(NeedsChecklist), new { admissionId });
            }

            _context.PatientNeeds.Add(new PatientNeed
            {
                AdmissionId = admissionId,
                NeedName = needName.Trim(),
                IsCompleted = false,
                SocialWorkerId = swId,
                UpdatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Need '{needName}' added.";
            return RedirectToAction(nameof(NeedsChecklist), new { admissionId });
        }

        // ==================================================================
        //  DELETE A NEED
        // ==================================================================
        [HttpPost("DeleteNeed/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteNeed(int needId, int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var need = await _context.PatientNeeds
                .FirstOrDefaultAsync(n => n.Id == needId && n.AdmissionId == admissionId);
            if (need == null) return NotFound();

            _context.PatientNeeds.Remove(need);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Need '{need.NeedName}' removed.";
            return RedirectToAction(nameof(NeedsChecklist), new { admissionId });
        }


        // ==================================================================
        //  DISCHARGE PLAN – ADVANCE STATUS
        // ==================================================================
        [HttpPost("AdvancePlanStatus/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdvancePlanStatus(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var plan = await _context.DischargePlans.FindAsync(id);
            if (plan == null || plan.IsActive != Status.Active) return NotFound();

            // Move to next status
            switch (plan.DischargeStatus)
            {
                case DischargePlanStatus.Pending:
                    plan.DischargeStatus = DischargePlanStatus.InProgress;
                    break;
                case DischargePlanStatus.InProgress:
                    plan.DischargeStatus = DischargePlanStatus.ReadyForReview;
                    break;
                case DischargePlanStatus.ReadyForReview:
                    plan.DischargeStatus = DischargePlanStatus.Approved;
                    break;
                case DischargePlanStatus.Approved:
                    plan.DischargeStatus = DischargePlanStatus.Implemented;
                    break;
                    // Already Implemented – no further advance
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Plan status updated to {plan.DischargeStatus}.";
            return RedirectToAction(nameof(PlansByAdmission), new { admissionId = plan.AdmissionId });
        }

        // ==================================================================
        //  DISCHARGE PLAN – REVERT STATUS (optional)
        // ==================================================================
        [HttpPost("RevertPlanStatus/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevertPlanStatus(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var plan = await _context.DischargePlans.FindAsync(id);
            if (plan == null || plan.IsActive != Status.Active) return NotFound();

            // Move to previous status
            switch (plan.DischargeStatus)
            {
                case DischargePlanStatus.InProgress:
                    plan.DischargeStatus = DischargePlanStatus.Pending;
                    break;
                case DischargePlanStatus.ReadyForReview:
                    plan.DischargeStatus = DischargePlanStatus.InProgress;
                    break;
                case DischargePlanStatus.Approved:
                    plan.DischargeStatus = DischargePlanStatus.ReadyForReview;
                    break;
                case DischargePlanStatus.Implemented:
                    plan.DischargeStatus = DischargePlanStatus.Approved;
                    break;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Plan status reverted to {plan.DischargeStatus}.";
            return RedirectToAction(nameof(PlansByAdmission), new { admissionId = plan.AdmissionId });
        }


        // ==================================================================
        //  DISCHARGE PLAN TASKS – LIST
        // ==================================================================

        [HttpGet("PlanTasks/{int:id}")]
        public async Task<IActionResult> PlanTasks(int planId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var plan = await _context.DischargePlans
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(p => p.Id == planId && p.IsActive == Status.Active);
            if (plan == null) return NotFound();

            ViewBag.PlanId = planId;
            ViewBag.PatientName = plan.Admission.Patient.FullName;
            ViewBag.PlanStatus = plan.DischargeStatus;

            var tasks = await _context.DischargePlanTasks
                .Where(t => t.DischargePlanId == planId && t.IsActive)
                .OrderBy(t => t.DueDate)
                .ToListAsync();

            return View(tasks);
        }

        // ==================================================================
        //  ADD TASK TO PLAN – GET
        // ==================================================================
        [HttpGet("AddTask/{int:id}")]
        public async Task<IActionResult> AddTask(int planId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var plan = await _context.DischargePlans
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(p => p.Id == planId && p.IsActive == Status.Active);
            if (plan == null) return NotFound();

            ViewBag.PatientName = plan.Admission.Patient.FullName;
            ViewBag.PlanId = planId;

            return View(new DischargePlanTask
            {
                DischargePlanId = planId
            });
        }

        // ==================================================================
        //  ADD TASK TO PLAN – POST
        // ==================================================================
        [HttpPost("AddTask/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTask(DischargePlanTask task)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("DischargePlan");
            ModelState.Remove("CompletedBySocialWorker");
            ModelState.Remove("IsActive");

            if (!ModelState.IsValid)
            {
                var plan = await _context.DischargePlans
                    .Include(p => p.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(p => p.Id == task.DischargePlanId);
                if (plan != null)
                {
                    ViewBag.PatientName = plan.Admission.Patient.FullName;
                    ViewBag.PlanId = task.DischargePlanId;
                }
                return View(task);
            }

            task.IsActive = true;
            task.CreatedAt = DateTime.Now;
            _context.DischargePlanTasks.Add(task);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task added to the plan.";
            return RedirectToAction(nameof(PlanTasks), new { planId = task.DischargePlanId });
        }

        // ==================================================================
        //  TOGGLE TASK COMPLETION
        // ==================================================================
        [HttpPost("ToggleTaskCompletion/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTaskCompletion(int taskId, int planId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var task = await _context.DischargePlanTasks
                .FirstOrDefaultAsync(t => t.Id == taskId && t.DischargePlanId == planId && t.IsActive);
            if (task == null) return NotFound();

            task.IsCompleted = !task.IsCompleted;
            if (task.IsCompleted)
            {
                task.CompletedAt = DateTime.Now;
                task.CompletedBySocialWorkerId = swId;
            }
            else
            {
                task.CompletedAt = null;
                task.CompletedBySocialWorkerId = null;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Task '{task.TaskName}' marked as {(task.IsCompleted ? "completed" : "incomplete")}.";
            return RedirectToAction(nameof(PlanTasks), new { planId });
        }

        // ==================================================================
        //  EDIT TASK – GET
        // ==================================================================
        [HttpGet("EditTask/{int:id}")]
        public async Task<IActionResult> EditTask(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var task = await _context.DischargePlanTasks
                .Include(t => t.DischargePlan).ThenInclude(p => p.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive);
            if (task == null) return NotFound();

            ViewBag.PatientName = task.DischargePlan.Admission.Patient.FullName;
            return View(task);
        }

        // ==================================================================
        //  EDIT TASK – POST
        // ==================================================================
        [HttpPost("EditTask/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTask(int id, DischargePlanTask posted)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            ModelState.Remove("DischargePlan");
            ModelState.Remove("CompletedBySocialWorker");
            ModelState.Remove("IsActive");

            if (!ModelState.IsValid)
            {
                var task = await _context.DischargePlanTasks
                    .Include(t => t.DischargePlan).ThenInclude(p => p.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(t => t.Id == id);
                if (task != null)
                    ViewBag.PatientName = task.DischargePlan.Admission.Patient.FullName;
                return View(posted);
            }

            var existing = await _context.DischargePlanTasks.FindAsync(id);
            if (existing == null || !existing.IsActive) return NotFound();

            existing.TaskName = posted.TaskName;
            existing.DueDate = posted.DueDate;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task updated.";
            return RedirectToAction(nameof(PlanTasks), new { planId = existing.DischargePlanId });
        }

        // ==================================================================
        //  DELETE TASK
        // ==================================================================
        [HttpPost("DeleteTask/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTask(int taskId, int planId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var task = await _context.DischargePlanTasks
                .FirstOrDefaultAsync(t => t.Id == taskId && t.DischargePlanId == planId && t.IsActive);
            if (task == null) return NotFound();

            task.IsActive = false;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task removed.";
            return RedirectToAction(nameof(PlanTasks), new { planId });
        }


        // ==================================================================
        //  REFERRALS – LIST FOR ADMISSION
        // ==================================================================

        [HttpGet("Referrals/{int:id}")]
        public async Task<IActionResult> Referrals(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            var referrals = await _context.Referrals
                .Include(r => r.SocialWorker)
                .Where(r => r.AdmissionId == admissionId && r.IsActive)
                .OrderByDescending(r => r.DateReferral)
                .ToListAsync();

            return View(referrals);
        }

        // ==================================================================
        //  ADD REFERRAL – GET
        // ==================================================================
        [HttpGet("AddReferral/{int:id}")]
        public async Task<IActionResult> AddReferral(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            return View(new Referral
            {
                AdmissionId = admissionId,
                SocialWorkerId = swId.Value,
                DateReferral = DateTime.Now
            });
        }

        // ==================================================================
        //  ADD REFERRAL – POST
        // ==================================================================
        [HttpPost("AddReferral/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddReferral(Referral referral)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == referral.AdmissionId);
                if (admission != null)
                    ViewBag.PatientName = admission.Patient.FullName;
                ViewBag.AdmissionId = referral.AdmissionId;
                return View(referral);
            }

            referral.SocialWorkerId = swId.Value;
            referral.IsActive = true;
            _context.Referrals.Add(referral);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Referral added.";
            return RedirectToAction(nameof(Referrals), new { admissionId = referral.AdmissionId });
        }

        // ==================================================================
        //  EDIT REFERRAL – GET
        // ==================================================================
        [HttpGet("EditReferral/{int:id}")]
        public async Task<IActionResult> EditReferral(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var referral = await _context.Referrals
                .Include(r => r.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);
            if (referral == null) return NotFound();

            ViewBag.PatientName = referral.Admission.Patient.FullName;
            return View(referral);
        }

        // ==================================================================
        //  EDIT REFERRAL – POST
        // ==================================================================
        [HttpPost("EditReferral/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditReferral(int id, Referral posted)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var referral = await _context.Referrals
                    .Include(r => r.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(r => r.Id == id);
                if (referral != null)
                    ViewBag.PatientName = referral.Admission.Patient.FullName;
                return View(posted);
            }

            var existing = await _context.Referrals.FindAsync(id);
            if (existing == null || !existing.IsActive) return NotFound();

            existing.OrganisationName = posted.OrganisationName;
            existing.ContactPerson = posted.ContactPerson;
            existing.ContactPhone = posted.ContactPhone;
            existing.ContactEmail = posted.ContactEmail;
            existing.Reason = posted.Reason;
            existing.Outcome = posted.Outcome;
            existing.OutcomeNotes = posted.OutcomeNotes;
            existing.DateReferral = posted.DateReferral;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Referral updated.";
            return RedirectToAction(nameof(Referrals), new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  DELETE REFERRAL
        // ==================================================================
        [HttpPost("DeleteReferral/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteReferral(int id, int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var referral = await _context.Referrals
                .FirstOrDefaultAsync(r => r.Id == id && r.AdmissionId == admissionId && r.IsActive);
            if (referral == null) return NotFound();

            referral.IsActive = false;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Referral removed.";
            return RedirectToAction(nameof(Referrals), new { admissionId });
        }

        // ==================================================================
        //  FOLLOW‑UPS – LIST FOR ADMISSION
        // ==================================================================

        [HttpGet("FollowUps/{int:id}")]
        public async Task<IActionResult> FollowUps(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            var followUps = await _context.FollowUps
                .Include(f => f.SocialWorker)
                .Where(f => f.AdmissionId == admissionId && f.IsActive)
                .OrderByDescending(f => f.ScheduledDate)
                .ToListAsync();

            return View(followUps);
        }

        // ==================================================================
        //  SCHEDULE FOLLOW‑UP – GET
        // ==================================================================
        [HttpGet("ScheduleFollowUp/{int:id}")]
        public async Task<IActionResult> ScheduleFollowUp(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Inactive); // discharged
            if (admission == null)
            {
                TempData["ErrorMessage"] = "You can only schedule a follow‑up for a discharged admission.";
                return RedirectToAction("MyAdmissions", "Patient"); // or back to Index
            }

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            return View(new FollowUp
            {
                AdmissionId = admissionId,
                SocialWorkerId = swId.Value,
                ScheduledDate = DateTime.Now.AddDays(7),
                Type = FollowUpType.PhoneCall
            });
        }

        // ==================================================================
        //  SCHEDULE FOLLOW‑UP – POST
        // ==================================================================
        [HttpPost("ScheduleFollowUp/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScheduleFollowUp(FollowUp followUp)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == followUp.AdmissionId);
                if (admission != null)
                    ViewBag.PatientName = admission.Patient.FullName;
                ViewBag.AdmissionId = followUp.AdmissionId;
                return View(followUp);
            }

            // Validate that the admission is discharged
            var validAdmission = await _context.Admissions
                .AnyAsync(a => a.Id == followUp.AdmissionId && a.IsActive == Status.Inactive);
            if (!validAdmission)
            {
                ModelState.AddModelError("", "Follow‑up can only be created for discharged admissions.");
                return View(followUp);
            }

            followUp.SocialWorkerId = swId.Value;
            followUp.CreatedAt = DateTime.Now;
            followUp.IsActive = true;
            _context.FollowUps.Add(followUp);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Follow‑up scheduled.";
            return RedirectToAction(nameof(FollowUps), new { admissionId = followUp.AdmissionId });
        }

        // ==================================================================
        //  EDIT FOLLOW‑UP – GET
        // ==================================================================
        [HttpGet("EditFollowUp/{int:id}")]
        public async Task<IActionResult> EditFollowUp(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var followUp = await _context.FollowUps
                .Include(f => f.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(f => f.Id == id && f.IsActive);
            if (followUp == null) return NotFound();

            ViewBag.PatientName = followUp.Admission.Patient.FullName;
            return View(followUp);
        }

        // ==================================================================
        //  EDIT FOLLOW‑UP – POST
        // ==================================================================
        [HttpPost("EditFollowUp/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFollowUp(int id, FollowUp posted)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var followUp = await _context.FollowUps
                    .Include(f => f.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(f => f.Id == id);
                if (followUp != null)
                    ViewBag.PatientName = followUp.Admission.Patient.FullName;
                return View(posted);
            }

            var existing = await _context.FollowUps.FindAsync(id);
            if (existing == null || !existing.IsActive) return NotFound();

            existing.ScheduledDate = posted.ScheduledDate;
            existing.Type = posted.Type;
            existing.Notes = posted.Notes;
            existing.Status = posted.Status;   // allow manual status change
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Follow‑up updated.";
            return RedirectToAction(nameof(FollowUps), new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  TOGGLE FOLLOW‑UP COMPLETION
        // ==================================================================
        [HttpPost("ToggleFollowUpCompletion/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleFollowUpCompletion(int id, int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var followUp = await _context.FollowUps
                .FirstOrDefaultAsync(f => f.Id == id && f.AdmissionId == admissionId && f.IsActive);
            if (followUp == null) return NotFound();

            if (followUp.Status == FollowUpStatus.Completed)
                followUp.Status = FollowUpStatus.Pending;
            else
                followUp.Status = FollowUpStatus.Completed;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Follow‑up marked as {followUp.Status}.";
            return RedirectToAction(nameof(FollowUps), new { admissionId });
        }

        // ==================================================================
        //  DELETE FOLLOW‑UP
        // ==================================================================
        [HttpPost("DeleteFollowUp/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFollowUp(int id, int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var followUp = await _context.FollowUps
                .FirstOrDefaultAsync(f => f.Id == id && f.AdmissionId == admissionId && f.IsActive);
            if (followUp == null) return NotFound();

            followUp.IsActive = false;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Follow‑up removed.";
            return RedirectToAction(nameof(FollowUps), new { admissionId });
        }



        // ==================================================================
        //  FAMILY CONTACT LOGS – LIST FOR ADMISSION
        // ==================================================================

        [HttpGet("FamilyContacts/{int:id}")]
        public async Task<IActionResult> FamilyContacts(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            var contacts = await _context.FamilyContactLogs
                .Include(c => c.SocialWorker)
                .Where(c => c.AdmissionId == admissionId && c.IsActive)
                .OrderByDescending(c => c.ContactDate)
                .ToListAsync();

            return View(contacts);
        }

        // ==================================================================
        //  ADD FAMILY CONTACT – GET
        // ==================================================================
        [HttpGet("AddFamilyContact/{int:id}")]
        public async Task<IActionResult> AddFamilyContact(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            return View(new FamilyContactLog
            {
                AdmissionId = admissionId,
                SocialWorkerId = swId.Value,
                ContactDate = DateTime.Now
            });
        }

        // ==================================================================
        //  ADD FAMILY CONTACT – POST
        // ==================================================================
        [HttpPost("AddFamilyContact/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddFamilyContact(FamilyContactLog log)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == log.AdmissionId);
                if (admission != null)
                    ViewBag.PatientName = admission.Patient.FullName;
                ViewBag.AdmissionId = log.AdmissionId;
                return View(log);
            }

            log.SocialWorkerId = swId.Value;
            log.CreatedAt = DateTime.Now;
            log.IsActive = true;
            _context.FamilyContactLogs.Add(log);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Family contact recorded.";
            return RedirectToAction(nameof(FamilyContacts), new { admissionId = log.AdmissionId });
        }

        // ==================================================================
        //  EDIT FAMILY CONTACT – GET
        // ==================================================================
        [HttpGet("EditFamilyContact/{int:id}")]
        public async Task<IActionResult> EditFamilyContact(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var log = await _context.FamilyContactLogs
                .Include(c => c.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(c => c.Id == id && c.IsActive);
            if (log == null) return NotFound();

            ViewBag.PatientName = log.Admission.Patient.FullName;
            return View(log);
        }

        // ==================================================================
        //  EDIT FAMILY CONTACT – POST
        // ==================================================================
        [HttpPost("EditFamilyContact/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFamilyContact(int id, FamilyContactLog posted)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();

            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");

            if (!ModelState.IsValid)
            {
                var log = await _context.FamilyContactLogs
                    .Include(c => c.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(c => c.Id == id);
                if (log != null)
                    ViewBag.PatientName = log.Admission.Patient.FullName;
                return View(posted);
            }

            var existing = await _context.FamilyContactLogs.FindAsync(id);
            if (existing == null || !existing.IsActive) return NotFound();

            existing.ContactName = posted.ContactName;
            existing.Relationship = posted.Relationship;
            existing.ContactDate = posted.ContactDate;
            existing.Notes = posted.Notes;
            existing.DecisionsMade = posted.DecisionsMade;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Contact updated.";
            return RedirectToAction(nameof(FamilyContacts), new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  DELETE FAMILY CONTACT
        // ==================================================================
        [HttpPost("DeleteFamilyContact/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFamilyContact(int id, int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var log = await _context.FamilyContactLogs
                .FirstOrDefaultAsync(c => c.Id == id && c.AdmissionId == admissionId && c.IsActive);
            if (log == null) return NotFound();

            log.IsActive = false;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Contact removed.";
            return RedirectToAction(nameof(FamilyContacts), new { admissionId });
        }


        // ==================================================================
        //  FAMILY MEETINGS – LIST FOR ADMISSION
        // ==================================================================

        [HttpGet("Meetings/{int:id}")]
        public async Task<IActionResult> Meetings(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            var meetings = await _context.FamilyMeetings
                .Include(m => m.Attendees).ThenInclude(a => a.Employee)
                .Where(m => m.AdmissionId == admissionId && m.IsActive)
                .OrderByDescending(m => m.ScheduledDate)
                .ToListAsync();

            return View(meetings);
        }

        // ==================================================================
        //  SCHEDULE FAMILY MEETING – GET
        // ==================================================================
        [HttpGet("ScheduleMeeting/{int:id}")]
        public async Task<IActionResult> ScheduleMeeting(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Include(a => a.Nurse)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.DoctorName = admission.Doctor?.FullName ?? "No doctor assigned";
            ViewBag.NurseName = admission.Nurse?.FullName ?? "No nurse assigned";

            // All active employees except the social worker? We'll let the social worker add any employee manually later.
            // For the initial form, we can optionally provide a multi-select list of all employees.
            ViewBag.AllEmployees = new MultiSelectList(
                await _context.Employees
                    .Where(e => e.IsActive == Status.Active && e.EmployeeID != swId)
                    .OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName");

            return View(new FamilyMeeting
            {
                AdmissionId = admissionId,
                SocialWorkerId = swId.Value,
                ScheduledDate = DateTime.Now.AddDays(2),
                Status = MeetingStatus.Scheduled
            });
        }

        // ==================================================================
        //  SCHEDULE FAMILY MEETING – POST
        // ==================================================================
        [HttpPost("ScheduleMeeting/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScheduleMeeting(FamilyMeeting meeting, int[]? extraEmployeeIds)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Admission");
            ModelState.Remove("SocialWorker");
            ModelState.Remove("Attendees");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .Include(a => a.Doctor)
                    .Include(a => a.Nurse)
                    .FirstOrDefaultAsync(a => a.Id == meeting.AdmissionId);
                if (admission != null)
                {
                    ViewBag.PatientName = admission.Patient.FullName;
                    ViewBag.DoctorName = admission.Doctor?.FullName ?? "No doctor assigned";
                    ViewBag.NurseName = admission.Nurse?.FullName ?? "No nurse assigned";
                }
                ViewBag.AllEmployees = new MultiSelectList(
                    await _context.Employees.Where(e => e.IsActive == Status.Active).OrderBy(e => e.LastName).ToListAsync(),
                    "EmployeeID", "FullName");
                return View(meeting);
            }

            // Validate admission
            var admissionDb = await _context.Admissions
                .Include(a => a.Doctor)
                .Include(a => a.Nurse)
                .FirstOrDefaultAsync(a => a.Id == meeting.AdmissionId && a.IsActive == Status.Active);
            if (admissionDb == null)
            {
                ModelState.AddModelError("", "Invalid or inactive admission.");
                return View(meeting);
            }

            meeting.SocialWorkerId = swId.Value;
            meeting.IsActive = true;

            // Auto-invite assigned doctor and nurse
            var attendees = new List<FamilyMeetingAttendee>();
            if (admissionDb.DoctorId > 0)
            {
                attendees.Add(new FamilyMeetingAttendee { EmployeeId = admissionDb.DoctorId });
            }
            if (admissionDb.NurseId.HasValue && admissionDb.NurseId.Value > 0)
            {
                attendees.Add(new FamilyMeetingAttendee { EmployeeId = admissionDb.NurseId.Value });
            }

            // Add any extra staff selected
            if (extraEmployeeIds != null)
            {
                foreach (var empId in extraEmployeeIds.Distinct())
                {
                    // Avoid adding duplicates (doctor/nurse already added)
                    if (!attendees.Any(a => a.EmployeeId == empId))
                    {
                        attendees.Add(new FamilyMeetingAttendee { EmployeeId = empId });
                    }
                }
            }

            meeting.Attendees = attendees;
            _context.FamilyMeetings.Add(meeting);
            await _context.SaveChangesAsync();

            // Notifications to all invitees (optional)
            try
            {
                string patientName = admissionDb.Patient?.FullName ?? "a patient";
                string swName = (await _context.Employees.FindAsync(swId.Value))?.FullName ?? "Social Worker";
                foreach (var att in attendees)
                {
                    string msg = $"{swName} scheduled a family meeting for {patientName} on {meeting.ScheduledDate:g}.";
                    await _notifService.NotifyUserAsync(att.EmployeeId, "Employee", msg,
                        Url.Action("Meetings", "SocialWorker", new { admissionId = meeting.AdmissionId }));
                }
            }
            catch { /* silent */ }

            TempData["SuccessMessage"] = "Family meeting scheduled and invitations sent.";
            return RedirectToAction(nameof(Meetings), new { admissionId = meeting.AdmissionId });
        }

        // ==================================================================
        //  MEETING DETAILS
        // ==================================================================

        [HttpGet("MeetingDetails/{int:id}")]
        public async Task<IActionResult> MeetingDetails(int id)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var meeting = await _context.FamilyMeetings
                .Include(m => m.Admission).ThenInclude(a => a.Patient)
                .Include(m => m.Attendees).ThenInclude(a => a.Employee)
                .FirstOrDefaultAsync(m => m.Id == id && m.IsActive);
            if (meeting == null) return NotFound();

            return View(meeting);
        }

        // ==================================================================
        //  CANCEL MEETING
        // ==================================================================
        [HttpPost("CancelMeeting/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelMeeting(int id, int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            var meeting = await _context.FamilyMeetings
                .FirstOrDefaultAsync(m => m.Id == id && m.AdmissionId == admissionId && m.IsActive);
            if (meeting == null) return NotFound();

            meeting.Status = MeetingStatus.Cancelled;
            // we keep IsActive true to show cancelled meetings
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Meeting cancelled.";
            return RedirectToAction(nameof(Meetings), new { admissionId });
        }

        // ==================================================================
        //  SOCIAL WORK REPORT (PDF)
        // ==================================================================
        [HttpGet("SocialWorkReport/{int:id}")]
        public async Task<IActionResult> SocialWorkReport(int admissionId)
        {
            int? swId = GetCurrentSocialWorkerId();
            if (swId == null) return RedirectToAction("Login", "Account");

            // Load admission with essential info
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .FirstOrDefaultAsync(a => a.Id == admissionId);
            if (admission == null) return NotFound();

            var vm = new SocialWorkReportViewModel
            {
                PatientName = admission.Patient?.FullName ?? "N/A",
                DateOfBirth = admission.Patient != null ? admission.Patient.DateOfBirth.ToString("dd MMM yyyy") : null,
                AdmissionDate = admission.AdmissionDate.ToString("dd MMM yyyy HH:mm"),
                DischargeDate = admission.DischargeDate?.ToString("dd MMM yyyy HH:mm"),
                DoctorName = admission.Doctor?.FullName,
                WardName = admission.Bed?.Ward?.Name
            };

            // Psychosocial assessment
            vm.Assessment = await _context.PsychosocialAssessments
                .FirstOrDefaultAsync(a => a.AdmissionId == admissionId && a.IsActive);

            // Discharge plans (active)
            vm.DischargePlans = await _context.DischargePlans
                .Include(dp => dp.SocialWorker)
                .Where(dp => dp.AdmissionId == admissionId && dp.IsActive == Status.Active)
                .OrderBy(dp => dp.CreatedAt)
                .ToListAsync();

            // Tasks across all active plans
            var planIds = vm.DischargePlans.Select(p => p.Id).ToList();
            if (planIds.Any())
            {
                vm.AllPlanTasks = await _context.DischargePlanTasks
                    .Where(t => planIds.Contains(t.DischargePlanId) && t.IsActive)
                    .OrderBy(t => t.DueDate)
                    .ToListAsync();
            }

            // Risk screenings
            vm.RiskScreenings = await _context.RiskScreenings
                .Include(r => r.SocialWorker)
                .Where(r => r.AdmissionId == admissionId && r.IsActive)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            // Referrals
            vm.Referrals = await _context.Referrals
                .Include(r => r.SocialWorker)
                .Where(r => r.AdmissionId == admissionId && r.IsActive)
                .OrderByDescending(r => r.DateReferral)
                .ToListAsync();

            // Needs checklist
            vm.NeedsChecklist = await _context.PatientNeeds
                .Where(n => n.AdmissionId == admissionId)
                .OrderBy(n => n.NeedName)
                .ToListAsync();

            // Follow‑ups
            vm.FollowUps = await _context.FollowUps
                .Include(f => f.SocialWorker)
                .Where(f => f.AdmissionId == admissionId && f.IsActive)
                .OrderByDescending(f => f.ScheduledDate)
                .ToListAsync();

            // Family contacts
            vm.FamilyContacts = await _context.FamilyContactLogs
                .Where(c => c.AdmissionId == admissionId && c.IsActive)
                .OrderByDescending(c => c.ContactDate)
                .ToListAsync();

            // Meetings
            vm.Meetings = await _context.FamilyMeetings
                .Include(m => m.Attendees).ThenInclude(a => a.Employee)
                .Where(m => m.AdmissionId == admissionId && m.IsActive)
                .OrderByDescending(m => m.ScheduledDate)
                .ToListAsync();

            // Render HTML view to PDF
            var html = await this.RenderViewAsync("SocialWorkReportPdf", vm, true);
            var pdf = HtmlToPdfConverter.Convert(html);
            return File(pdf, "application/pdf", $"SocialWorkReport_{admissionId}.pdf");
        }


        private static string CalculateRiskLevel(int score)
        {
            if (score >= 70) return "High";
            if (score >= 40) return "Medium";
            return "Low";
        }



    }
}