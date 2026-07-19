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
    [Authorize(Roles = "DOCTOR")]
    public class DoctorController : Controller
    {
        private readonly WardDbContext _context;
        private readonly INotificationService _notifService;

        public DoctorController(WardDbContext context, INotificationService notifService)
        {
            _context = context;
            _notifService = notifService;
        }

        private int? GetCurrentDoctorId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            return id;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD – Clinical Decision Dashboard
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            ViewBag.ActivePatients = await _context.Admissions.CountAsync(a => a.DoctorId == doctorId && a.IsActive == Status.Active);
            ViewBag.UpcomingVisits = await _context.DoctorVisits.CountAsync(dv => dv.DoctorId == doctorId && dv.IsActive == Status.Active && dv.VisitDate > DateTime.Now);

            var urgentAdmissionIds = await GetAdmissionIdsWithCriticalVitalsAsync(doctorId.Value);
            ViewBag.UrgentReviewCount = urgentAdmissionIds.Count;

            var abnormalAdmissionIds = await GetAdmissionIdsWithAbnormalVitalsAsync(doctorId.Value);
            ViewBag.AbnormalVitalsCount = abnormalAdmissionIds.Count;

            ViewBag.DischargedTodayCount = await _context.Admissions
                .Where(a => a.DoctorId == doctorId && a.IsActive == Status.Inactive
                           && a.DischargeDate.HasValue
                           && a.DischargeDate.Value.Date == DateTime.Today)
                .CountAsync();

            ViewBag.PendingPrescriptionsCount = await _context.Prescriptions
                .Where(p => p.Admission.DoctorId == doctorId
                           && p.IsActive == Status.Active
                           && p.ScriptStatus == ScriptStatus.New)
                .CountAsync();

            ViewBag.TodayVisitsCount = await _context.DoctorVisits
                .Where(dv => dv.DoctorId == doctorId
                            && dv.IsActive == Status.Active
                            && dv.VisitDate.Date == DateTime.Today)
                .CountAsync();

            ViewBag.AllergyPatientsCount = await _context.Admissions
                .Where(a => a.DoctorId == doctorId && a.IsActive == Status.Active)
                .AnyAsync(a => a.AdmissionAllergies.Any())
                ? await _context.Admissions
                    .Where(a => a.DoctorId == doctorId && a.IsActive == Status.Active)
                    .CountAsync(a => a.AdmissionAllergies.Any())
                : 0;

            var today = DateTime.Today;
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var startOfWeek = today.AddDays(-diff).Date;
            var endOfWeek = startOfWeek.AddDays(7);

            int dischargesThisWeek = await _context.Admissions
                .Where(a => a.DoctorId == doctorId
                           && a.IsActive == Status.Inactive
                           && a.DischargeDate >= startOfWeek
                           && a.DischargeDate < endOfWeek)
                .CountAsync();

            int admissionsThisWeek = await _context.Admissions
                .Where(a => a.DoctorId == doctorId
                           && a.AdmissionDate >= startOfWeek
                           && a.AdmissionDate < endOfWeek)
                .CountAsync();

            ViewBag.RecoveryRate = admissionsThisWeek > 0
                ? Math.Round((double)dischargesThisWeek / admissionsThisWeek * 100, 0)
                : 0;

            return View();
        }

        // ==================================================================
        //  DOCTOR TASK LIST – Actionable items for today
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> TaskList()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var tasks = new List<(string Description, string Link, string Icon, string Priority)>();

            // 1. Today's scheduled visits
            var todayVisits = await _context.DoctorVisits
                .Include(v => v.Admission).ThenInclude(a => a.Patient)
                .Where(v => v.DoctorId == doctorId
                           && v.IsActive == Status.Active
                           && v.VisitDate.Date == DateTime.Today)
                .OrderBy(v => v.VisitDate)
                .ToListAsync();

            foreach (var visit in todayVisits)
            {
                string patientName = visit.Admission?.Patient?.FullName ?? "Unknown";
                string time = visit.VisitDate.ToString("HH:mm");
                tasks.Add((
                    $"Visit {patientName} at {time}",
                    Url.Action("PatientFolder", new { admissionId = visit.AdmissionId }),
                    "fa-calendar-check",
                    "High"
                ));
            }

            // 2. Patients with critical vitals (urgent review)
            var urgentIds = await GetAdmissionIdsWithCriticalVitalsAsync(doctorId.Value);
            if (urgentIds.Any())
            {
                var urgentPatients = await _context.Admissions
                    .Include(a => a.Patient)
                    .Where(a => urgentIds.Contains(a.Id))
                    .ToListAsync();

                foreach (var adm in urgentPatients)
                {
                    tasks.Add((
                        $"URGENT: Review vitals for {adm.Patient.FullName}",
                        Url.Action("PatientFolder", new { admissionId = adm.Id }),
                        "fa-triangle-exclamation",
                        "Critical"
                    ));
                }
            }

            // 3. Patients with abnormal vitals (non‑critical)
            var abnormalIds = await GetAdmissionIdsWithAbnormalVitalsAsync(doctorId.Value);
            if (abnormalIds.Any())
            {
                var abnormalPatients = await _context.Admissions
                    .Include(a => a.Patient)
                    .Where(a => abnormalIds.Contains(a.Id) && !urgentIds.Contains(a.Id))
                    .ToListAsync();

                foreach (var adm in abnormalPatients)
                {
                    tasks.Add((
                        $"Check vitals for {adm.Patient.FullName}",
                        Url.Action("PatientFolder", new { admissionId = adm.Id }),
                        "fa-heart-pulse",
                        "Medium"
                    ));
                }
            }

            // 4. Pending prescriptions (still “New”)
            var pendingScripts = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Where(p => p.Admission.DoctorId == doctorId
                           && p.IsActive == Status.Active
                           && p.ScriptStatus == ScriptStatus.New)
                .ToListAsync();

            foreach (var script in pendingScripts)
            {
                tasks.Add((
                    $"Prescription pending: {script.Medication?.Name ?? "Unknown"} for {script.Admission?.Patient?.FullName}",
                    Url.Action("PrescriptionsByAdmission", new { admissionId = script.AdmissionId }),
                    "fa-prescription-bottle-medical",
                    "Medium"
                ));
            }

            // 5. Visits where instructions haven’t been written yet
            var visitsWithoutInstructions = await _context.DoctorVisits
                .Include(v => v.Admission).ThenInclude(a => a.Patient)
                .Where(v => v.DoctorId == doctorId
                           && v.IsActive == Status.Active
                           && string.IsNullOrEmpty(v.Instructions)
                           && v.VisitDate.Date == DateTime.Today)
                .ToListAsync();

            foreach (var visit in visitsWithoutInstructions)
            {
                tasks.Add((
                    $"Write instructions for {visit.Admission?.Patient?.FullName}",
                    Url.Action("WriteInstructions", new { visitId = visit.Id }),
                    "fa-pen-to-square",
                    "Medium"
                ));
            }

            // Store tasks in ViewBag for the view
            ViewBag.Tasks = tasks;
            return View();
        }
        // ==================================================================
        //  CLINICAL TIMELINE – All patient events in chronological order
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ClinicalTimeline(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);

            if (admission == null) return NotFound();

            // Gather all events (filter out null timestamps where applicable)
            var vitals = await _context.Vitals
                .Where(v => v.AdmissionId == admissionId && v.IsActive == Status.Active)
                .OrderBy(v => v.DateRecorded)
                .ToListAsync();

            var medAdmins = await _context.MedicationAdministrations
                .Include(ma => ma.Medication)
                .Where(ma => ma.AdmissionId == admissionId && ma.IsActive == Status.Active)
                .OrderBy(ma => ma.DateAdministered)
                .ToListAsync();

            var visits = await _context.DoctorVisits
                .Include(dv => dv.Doctor)
                .Where(dv => dv.AdmissionId == admissionId && dv.IsActive == Status.Active)
                .OrderBy(dv => dv.VisitDate)
                .ToListAsync();

            var treatments = await _context.Treatments
                .Where(t => t.AdmissionId == admissionId && t.IsActive == Status.Active)
                .OrderBy(t => t.TreatmentDate)
                .ToListAsync();

            // Only include movements with a valid timestamp
            var movements = await _context.PatientMovements
                .Where(pm => pm.AdmissionId == admissionId && pm.Timestamp.HasValue)
                .OrderBy(pm => pm.Timestamp)
                .ToListAsync();

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Medication)
                .Where(p => p.AdmissionId == admissionId && p.IsActive == Status.Active)
                .OrderBy(p => p.PrescribedDate)
                .ToListAsync();

            // Build timeline entries
            var timelineEntries = new List<ClinicalTimelineEntry>();

            foreach (var v in vitals)
            {
                string details = $"BP {v.BloodPressure}, Temp {v.TemperatureCelsius}°C, HR {v.HeartRateBpm}";
                if (v.RespiratoryRate.HasValue) details += $", RR {v.RespiratoryRate}";
                if (v.OxygenSaturation.HasValue) details += $", SpO₂ {v.OxygenSaturation}%";
                if (v.BloodSugarMmolL.HasValue) details += $", Sugar {v.BloodSugarMmolL} mmol/L";
                timelineEntries.Add(new ClinicalTimelineEntry
                {
                    Timestamp = v.DateRecorded,
                    EventType = "Vitals",
                    Description = $"Vitals recorded: {details}",
                    Icon = "fa-heartbeat"
                });
            }

            foreach (var ma in medAdmins)
            {
                timelineEntries.Add(new ClinicalTimelineEntry
                {
                    Timestamp = ma.DateAdministered,
                    EventType = "Medication",
                    Description = $"Medication administered: {ma.Medication?.Name} ({ma.Dosage})",
                    Icon = "fa-capsules"
                });
            }

            foreach (var dv in visits)
            {
                timelineEntries.Add(new ClinicalTimelineEntry
                {
                    Timestamp = dv.VisitDate,
                    EventType = "Doctor Visit",
                    Description = $"Visit by Dr. {dv.Doctor?.FullName ?? "Unknown"}. {dv.Notes}",
                    Icon = "fa-user-doctor"
                });
            }

            foreach (var t in treatments)
            {
                timelineEntries.Add(new ClinicalTimelineEntry
                {
                    Timestamp = t.TreatmentDate,
                    EventType = "Treatment",
                    Description = $"Treatment: {t.TreatmentType} – {t.Notes}",
                    Icon = "fa-syringe"
                });
            }

            // Corrected movement entries – using MovementType and Location
            foreach (var pm in movements)
            {
                timelineEntries.Add(new ClinicalTimelineEntry
                {
                    Timestamp = pm.Timestamp.Value,   // safe because we filtered HasValue
                    EventType = "Movement",
                    Description = $"Patient movement: {pm.MovementType} to {pm.Location}",
                    Icon = "fa-ambulance"
                });
            }

            foreach (var p in prescriptions)
            {
                timelineEntries.Add(new ClinicalTimelineEntry
                {
                    Timestamp = p.PrescribedDate,
                    EventType = "Prescription",
                    Description = $"Prescribed {p.Medication?.Name} – {p.Dosage}, {p.Frequency}",
                    Icon = "fa-prescription"
                });
            }

            // Sort chronologically (oldest first)
            timelineEntries = timelineEntries.OrderBy(e => e.Timestamp).ToList();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            return View(timelineEntries);
        }

        // ==================================================================
        //  START VISIT (Timer)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartVisit(int visitId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == visitId && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);

            if (visit == null) return NotFound();

            visit.StartVisitTime = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit timer started.";
            return RedirectToAction("VisitDetails", new { id = visitId });
        }

        // ==================================================================
        //  END VISIT (Stop Timer)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EndVisit(int visitId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == visitId && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);

            if (visit == null) return NotFound();

            if (visit.StartVisitTime == null)
            {
                TempData["ErrorMessage"] = "You must start the visit timer first.";
                return RedirectToAction("VisitDetails", new { id = visitId });
            }

            visit.EndVisitTime = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit ended. Duration saved.";
            return RedirectToAction("VisitDetails", new { id = visitId });
        }


        // ==================================================================
        //  MONTHLY VISIT CALENDAR – upcoming visits in a calendar grid
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> MyVisitCalendar(int? month, int? year)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var today = DateTime.Today;
            int displayMonth = month ?? today.Month;
            int displayYear = year ?? today.Year;

            // Fetch all future visits (including today)
            var visits = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .Where(dv => dv.DoctorId == doctorId.Value
                            && dv.IsActive == Status.Active
                            && dv.VisitDate >= today)
                .OrderBy(dv => dv.VisitDate)
                .ToListAsync();

            var visitsByDate = visits.GroupBy(v => v.VisitDate.Date)
                                    .ToDictionary(g => g.Key, g => g.ToList());

            ViewBag.DisplayMonth = displayMonth;
            ViewBag.DisplayYear = displayYear;
            ViewBag.VisitsByDate = visitsByDate;

            return View();
        }


        // ==================== EXISTING ACTION METHODS (UrgentPatients, etc.) ====================
        [HttpGet]
        public async Task<IActionResult> UrgentPatients()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var ids = await GetAdmissionIdsWithCriticalVitalsAsync(doctorId.Value);
            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => ids.Contains(a.Id))
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();

            ViewBag.Title = "Patients Needing Urgent Review";
            ViewBag.Subtitle = "Critical vital signs requiring immediate attention.";
            return View("PatientAlertList", admissions);
        }

        [HttpGet]
        public async Task<IActionResult> AbnormalVitalsPatients()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var ids = await GetAdmissionIdsWithAbnormalVitalsAsync(doctorId.Value);
            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => ids.Contains(a.Id))
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();

            ViewBag.Title = "Patients with Abnormal Vitals";
            ViewBag.Subtitle = "Any vital sign outside the normal range.";
            return View("PatientAlertList", admissions);
        }

        [HttpGet]
        public async Task<IActionResult> DischargedToday()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => a.DoctorId == doctorId
                           && a.IsActive == Status.Inactive
                           && a.DischargeDate.HasValue
                           && a.DischargeDate.Value.Date == DateTime.Today)
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();

            ViewBag.Title = "Patients Discharged Today";
            ViewBag.Subtitle = "Admissions closed today.";
            return View("PatientAlertList", admissions);
        }

        [HttpGet]
        public async Task<IActionResult> PendingPrescriptions()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Medication)
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Where(p => p.Admission.DoctorId == doctorId
                           && p.IsActive == Status.Active
                           && p.ScriptStatus == ScriptStatus.New)
                .OrderBy(p => p.PrescribedDate)
                .ToListAsync();

            ViewBag.Title = "Prescriptions Pending Script Manager";
            ViewBag.Subtitle = "These prescriptions are still in 'New' status.";
            return View("PendingPrescriptionsList", prescriptions);
        }

        [HttpGet]
        public async Task<IActionResult> TodayVisits()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visits = await _context.DoctorVisits
                .Include(v => v.Admission).ThenInclude(a => a.Patient)
                .Include(v => v.Admission.Bed).ThenInclude(b => b.Ward)
                .Where(v => v.DoctorId == doctorId
                           && v.IsActive == Status.Active
                           && v.VisitDate.Date == DateTime.Today)
                .OrderBy(v => v.VisitDate)
                .ToListAsync();

            ViewBag.Title = "Today's Scheduled Visits";
            ViewBag.Subtitle = "All your visits for today.";
            return View("TodayVisitsList", visits);
        }

        [HttpGet]
        public async Task<IActionResult> PriorityQueue()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            // Get all active admissions for this doctor
            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => a.DoctorId == doctorId.Value && a.IsActive == Status.Active)
                .ToListAsync();

            var priorityList = new List<(Admission Admission, string Priority, int Score)>();

            foreach (var adm in admissions)
            {
                var latestVitals = await _context.Vitals
                    .Where(v => v.AdmissionId == adm.Id && v.IsActive == Status.Active)
                    .OrderByDescending(v => v.DateRecorded)
                    .FirstOrDefaultAsync();

                int score = 0;
                string priority = "Low";

                if (latestVitals != null)
                {
                    // Blood Pressure
                    if (!string.IsNullOrEmpty(latestVitals.BloodPressure))
                    {
                        var parts = latestVitals.BloodPressure.Split('/');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out int sys) &&
                            int.TryParse(parts[1], out int dia))
                        {
                            if (sys > 180 || dia > 120) { score += 4; priority = "Critical"; }
                            else if (sys > 140 || sys < 90 || dia > 90 || dia < 60) { score += 2; priority = "High"; }
                        }
                    }

                    // Temperature
                    if (latestVitals.TemperatureCelsius.HasValue)
                    {
                        var temp = latestVitals.TemperatureCelsius.Value;
                        if (temp > 39.0m) { score += 4; priority = "Critical"; }
                        else if (temp > 37.5m || temp < 36.0m) { score += 2; if (priority != "Critical") priority = "High"; }
                    }

                    // Heart Rate
                    if (latestVitals.HeartRateBpm.HasValue)
                    {
                        var hr = latestVitals.HeartRateBpm.Value;
                        if (hr > 120) { score += 4; priority = "Critical"; }
                        else if (hr > 100 || hr < 60) { score += 2; if (priority != "Critical") priority = "High"; }
                    }

                    // Oxygen Saturation
                    if (latestVitals.OxygenSaturation.HasValue)
                    {
                        var spo2 = latestVitals.OxygenSaturation.Value;
                        if (spo2 < 90) { score += 4; priority = "Critical"; }
                        else if (spo2 < 95) { score += 2; if (priority != "Critical") priority = "High"; }
                    }

                    // Respiratory Rate (additional)
                    if (latestVitals.RespiratoryRate.HasValue)
                    {
                        var rr = latestVitals.RespiratoryRate.Value;
                        if (rr > 30 || rr < 8) { score += 4; priority = "Critical"; }
                        else if (rr > 20 || rr < 12) { score += 1; }
                    }

                    // Blood Sugar
                    if (latestVitals.BloodSugarMmolL.HasValue)
                    {
                        var sugar = latestVitals.BloodSugarMmolL.Value;
                        if (sugar > 22 || sugar < 3) { score += 4; priority = "Critical"; }
                        else if (sugar > 11 || sugar < 4) { score += 1; }
                    }
                }
                else
                {
                    // No vitals recorded – medium priority by default
                    priority = "Medium";
                    score = 0;
                }

                // If priority is not yet Critical but score is high, adjust
                if (priority != "Critical" && score >= 6) priority = "High";
                else if (priority != "Critical" && priority != "High" && score >= 3) priority = "Medium";

                priorityList.Add((adm, priority, score));
            }

            // Sort: Critical first, then High, Medium, Low – by score descending within each group
            var sorted = priorityList
                .OrderByDescending(p => p.Priority == "Critical" ? 4 : p.Priority == "High" ? 3 : p.Priority == "Medium" ? 2 : 1)
                .ThenByDescending(p => p.Score)
                .ToList();

            return View(sorted);
        }


        // ==================================================================
        //  PRINTABLE MEDICAL REPORT
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PrintReport(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value);

            if (admission == null) return NotFound();

            ViewBag.Vitals = await _context.Vitals
                .Where(v => v.AdmissionId == admissionId && v.IsActive == Status.Active)
                .OrderBy(v => v.DateRecorded).ToListAsync();

            ViewBag.Treatments = await _context.Treatments
                .Where(t => t.AdmissionId == admissionId && t.IsActive == Status.Active)
                .OrderBy(t => t.TreatmentDate).ToListAsync();

            ViewBag.Prescriptions = await _context.Prescriptions
                .Include(p => p.Medication)
                .Where(p => p.AdmissionId == admissionId && p.IsActive == Status.Active)
                .OrderBy(p => p.PrescribedDate).ToListAsync();

            ViewBag.DoctorVisits = await _context.DoctorVisits
                .Include(dv => dv.Doctor)
                .Include(dv => dv.AcknowledgedBy)
                .Where(dv => dv.AdmissionId == admissionId && dv.IsActive == Status.Active)
                .OrderBy(dv => dv.VisitDate).ToListAsync();

            // Discharge summary
            var dischargeVisit = ((List<DoctorVisit>)ViewBag.DoctorVisits)?
                .FirstOrDefault(v => v.Notes != null && v.Notes.Contains("Discharge ordered"));
            ViewBag.DischargeInstructions = dischargeVisit?.Instructions;
            ViewBag.DischargeDate = admission.DischargeDate;

            return View(admission);
        }

        // ==================================================================
        //  RESCHEDULE VISIT (GET)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> RescheduleVisit(int visitId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(dv => dv.Id == visitId && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);

            if (visit == null) return NotFound();

            ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
            return View(visit);
        }

        // ==================================================================
        //  RESCHEDULE VISIT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RescheduleVisit(int id, DoctorVisit posted)
        {
            if (id != posted.Id) return BadRequest();

            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var existing = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);

            if (existing == null) return NotFound();

            // Validate that the new date is valid (future for rescheduling sense, but allow any date)
            if (posted.VisitDate < DateTime.Now)
            {
                ModelState.AddModelError("VisitDate", "The new date/time cannot be in the past.");
                var visit = await _context.DoctorVisits
                    .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(dv => dv.Id == id);
                if (visit != null)
                    ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
                return View(posted);
            }

            // Update the visit date and reset timer fields (old timing no longer valid)
            existing.VisitDate = posted.VisitDate;
            existing.StartVisitTime = null;
            existing.EndVisitTime = null;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit rescheduled successfully.";
            return RedirectToAction("PatientFolder", new { admissionId = existing.AdmissionId });
        }

        // ==================================================================
        //  ABNORMAL VITAL ALERTS for a specific patient
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> VitalAlerts(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);

            if (admission == null) return NotFound();

            var latestVitals = await _context.Vitals
                .Where(v => v.AdmissionId == admissionId && v.IsActive == Status.Active)
                .OrderByDescending(v => v.DateRecorded)
                .FirstOrDefaultAsync();

            // Prepare a list of alert objects
            var alerts = new List<VitalAlertViewModel>();

            if (latestVitals != null)
            {
                // Blood Pressure
                if (!string.IsNullOrEmpty(latestVitals.BloodPressure))
                {
                    var parts = latestVitals.BloodPressure.Split('/');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int sys) && int.TryParse(parts[1], out int dia))
                    {
                        if (sys > 180 || dia > 120)
                            alerts.Add(new VitalAlertViewModel { Parameter = "Blood Pressure", Value = latestVitals.BloodPressure, Severity = "Critical", Message = "Severe hypertension", Icon = "fa-heart-pulse" });
                        else if (sys > 140 || dia > 90)
                            alerts.Add(new VitalAlertViewModel { Parameter = "Blood Pressure", Value = latestVitals.BloodPressure, Severity = "High", Message = "High blood pressure", Icon = "fa-heart-pulse" });
                        else if (sys < 90 || dia < 60)
                            alerts.Add(new VitalAlertViewModel { Parameter = "Blood Pressure", Value = latestVitals.BloodPressure, Severity = "Low", Message = "Low blood pressure", Icon = "fa-heart-pulse" });
                    }
                }

                // Temperature
                if (latestVitals.TemperatureCelsius.HasValue)
                {
                    var temp = latestVitals.TemperatureCelsius.Value;
                    if (temp > 39.0m)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Temperature", Value = $"{temp}°C", Severity = "Critical", Message = "High fever", Icon = "fa-temperature-high" });
                    else if (temp > 37.5m)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Temperature", Value = $"{temp}°C", Severity = "Warning", Message = "Elevated temperature", Icon = "fa-temperature-arrow-up" });
                    else if (temp < 36.0m)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Temperature", Value = $"{temp}°C", Severity = "Warning", Message = "Low temperature", Icon = "fa-temperature-low" });
                }

                // Heart Rate
                if (latestVitals.HeartRateBpm.HasValue)
                {
                    var hr = latestVitals.HeartRateBpm.Value;
                    if (hr > 120)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Heart Rate", Value = $"{hr} bpm", Severity = "Critical", Message = "Tachycardia", Icon = "fa-heart-circle-bolt" });
                    else if (hr > 100)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Heart Rate", Value = $"{hr} bpm", Severity = "Warning", Message = "Rapid pulse", Icon = "fa-heart" });
                    else if (hr < 60)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Heart Rate", Value = $"{hr} bpm", Severity = "Warning", Message = "Slow pulse (bradycardia)", Icon = "fa-heart" });
                }

                // Oxygen Saturation
                if (latestVitals.OxygenSaturation.HasValue)
                {
                    var spo2 = latestVitals.OxygenSaturation.Value;
                    if (spo2 < 90)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Oxygen Saturation", Value = $"{spo2}%", Severity = "Critical", Message = "Severe hypoxia", Icon = "fa-lungs" });
                    else if (spo2 < 95)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Oxygen Saturation", Value = $"{spo2}%", Severity = "Warning", Message = "Low oxygen", Icon = "fa-lungs" });
                }

                // Respiratory Rate
                if (latestVitals.RespiratoryRate.HasValue)
                {
                    var rr = latestVitals.RespiratoryRate.Value;
                    if (rr > 30)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Respiratory Rate", Value = $"{rr}/min", Severity = "Critical", Message = "Tachypnea", Icon = "fa-lungs" });
                    else if (rr > 20)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Respiratory Rate", Value = $"{rr}/min", Severity = "Warning", Message = "Elevated breathing rate", Icon = "fa-lungs" });
                    else if (rr < 12)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Respiratory Rate", Value = $"{rr}/min", Severity = "Warning", Message = "Bradypnea", Icon = "fa-lungs" });
                }

                // Blood Sugar
                if (latestVitals.BloodSugarMmolL.HasValue)
                {
                    var sugar = latestVitals.BloodSugarMmolL.Value;
                    if (sugar > 22)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Blood Sugar", Value = $"{sugar} mmol/L", Severity = "Critical", Message = "Severe hyperglycemia", Icon = "fa-droplet" });
                    else if (sugar > 11)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Blood Sugar", Value = $"{sugar} mmol/L", Severity = "Warning", Message = "High blood sugar", Icon = "fa-droplet" });
                    else if (sugar < 3)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Blood Sugar", Value = $"{sugar} mmol/L", Severity = "Critical", Message = "Severe hypoglycemia", Icon = "fa-droplet" });
                    else if (sugar < 4)
                        alerts.Add(new VitalAlertViewModel { Parameter = "Blood Sugar", Value = $"{sugar} mmol/L", Severity = "Warning", Message = "Low blood sugar", Icon = "fa-droplet" });
                }

                ViewBag.TimeAgo = DateTime.Now.Subtract(latestVitals.DateRecorded).TotalHours > 1
                    ? $"{(int)DateTime.Now.Subtract(latestVitals.DateRecorded).TotalHours} hours ago"
                    : "just now";
            }
            else
            {
                ViewBag.TimeAgo = null;
            }

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            return View(alerts);
        }

        // ==================================================================
        //  MISSED VISITS – scheduled visits not started and now in the past
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> MissedVisits()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var now = DateTime.Now;

            var missed = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .Where(dv => dv.DoctorId == doctorId.Value
                            && dv.IsActive == Status.Active
                            && dv.VisitDate < now                     // scheduled time already passed
                            && dv.StartVisitTime == null)             // doctor never pressed Start
                .OrderByDescending(dv => dv.VisitDate)
                .ToListAsync();

            ViewBag.Title = "Missed Visits";
            ViewBag.Subtitle = "Scheduled visits that were not started on time.";

            return View(missed);
        }


        // ==================================================================
        //  SCHEDULE FOLLOW‑UP APPOINTMENT (GET)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ScheduleFollowUp(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            // The admission must belong to this doctor (can be active or discharged)
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value);

            if (admission == null) return NotFound();

            ViewBag.PatientName = admission.Patient.FullName;

            // Dropdown for optional assigned doctor
            ViewBag.Doctors = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                    .OrderBy(e => e.LastName)
                    .ToListAsync(),
                "EmployeeID", "FullName");

            return View(new FollowUpAppointment
            {
                AdmissionId = admissionId,
                PatientId = admission.PatientId,
                CreatedByDoctorId = doctorId.Value,
                AppointmentDate = DateTime.Now.AddDays(7), // default 7 days later
                Location = "Outpatient Clinic"
            });
        }

        // ==================================================================
        //  SCHEDULE FOLLOW‑UP APPOINTMENT (POST)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScheduleFollowUp(FollowUpAppointment appointment)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            // Remove navigation properties from model validation
            ModelState.Remove("Admission");
            ModelState.Remove("Patient");
            ModelState.Remove("CreatedByDoctor");
            ModelState.Remove("AssignedDoctor");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == appointment.AdmissionId);
                if (admission != null)
                    ViewBag.PatientName = admission.Patient.FullName;

                ViewBag.Doctors = new SelectList(
                    await _context.Employees
                        .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                        .OrderBy(e => e.LastName)
                        .ToListAsync(),
                    "EmployeeID", "FullName", appointment.AssignedDoctorId);

                return View(appointment);
            }

            appointment.CreatedByDoctorId = doctorId.Value;
            _context.FollowUpAppointments.Add(appointment);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Follow‑up appointment scheduled.";
            return RedirectToAction("PatientFolder", new { admissionId = appointment.AdmissionId });
        }

        // ==================================================================
        //  LIST FOLLOW‑UP APPOINTMENTS FOR AN ADMISSION (or all)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> FollowUpAppointments(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value);

            if (admission == null) return NotFound();

            var appointments = await _context.FollowUpAppointments
                .Include(f => f.AssignedDoctor)
                .Where(f => f.AdmissionId == admissionId)
                .OrderBy(f => f.AppointmentDate)
                .ToListAsync();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            return View(appointments);
        }


        // ==================================================================
        //  PATIENT PROGRESS GRAPHS – Vital sign trends
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ProgressGraphs(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);

            if (admission == null) return NotFound();

            var vitals = await _context.Vitals
                .Where(v => v.AdmissionId == admissionId && v.IsActive == Status.Active)
                .OrderBy(v => v.DateRecorded)
                .ToListAsync();

            ViewBag.PatientName = admission.Patient.FullName;
            ViewBag.AdmissionId = admissionId;

            return View(vitals);
        }


        [HttpGet]
        public async Task<IActionResult> PatientsWithAllergies()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Where(a => a.DoctorId == doctorId
                           && a.IsActive == Status.Active
                           && a.AdmissionAllergies.Any())
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();

            ViewBag.Title = "Patients with Allergies";
            ViewBag.Subtitle = "Active admissions that have recorded allergies.";
            return View("PatientAlertList", admissions);
        }

        // ==================================================================
        //  MY PATIENTS – list of admissions assigned to this doctor
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> MyPatients()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => a.DoctorId == doctorId.Value && a.IsActive == Status.Active)
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();

            return View(admissions);
        }

        [HttpGet]
        public async Task<IActionResult> PatientFolder(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Include(a => a.Doctor)
                .Include(a => a.AdmissionAllergies).ThenInclude(aa => aa.Allergy)
                .Include(a => a.AdmissionMedications).ThenInclude(am => am.Medication)
                .Include(a => a.AdmissionConditions).ThenInclude(ac => ac.Condition)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value);

            if (admission == null) return NotFound();

            // ---- Medical History Summary ----
            var patient = admission.Patient;
            if (patient != null)
            {
                // Age – use MinValue as "not set"
                if (patient.DateOfBirth != DateTime.MinValue)
                {
                    var today = DateTime.Today;
                    int age = today.Year - patient.DateOfBirth.Year;
                    if (patient.DateOfBirth.Date > today.AddYears(-age)) age--;
                    ViewBag.PatientAge = age;
                }
                else
                {
                    ViewBag.PatientAge = null;
                }

                // Conditions check
                var conditionNames = admission.AdmissionConditions
                    .Select(ac => ac.Condition?.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Select(name => name!.ToLowerInvariant())
                    .ToList();

                ViewBag.IsDiabetic = conditionNames.Contains("diabetes") || conditionNames.Contains("diabetic");
                ViewBag.HasHypertension = conditionNames.Contains("hypertension");
                ViewBag.HasAsthma = conditionNames.Contains("asthma");

                ViewBag.PreviousAdmissions = await _context.Admissions
                    .Where(a => a.PatientId == admission.PatientId && a.Id != admissionId)
                    .CountAsync();

                ViewBag.CurrentAllergies = admission.AdmissionAllergies.Count;
            }

            // ---- Vital / treatment / visit data ----
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

            ViewBag.PatientMovements = await _context.PatientMovements
                .Where(pm => pm.AdmissionId == admissionId)
                .OrderByDescending(pm => pm.Timestamp).ToListAsync();

            ViewBag.DoctorVisits = await _context.DoctorVisits
    .Include(dv => dv.Doctor)
    .Include(dv => dv.AcknowledgedBy)   // correct navigation property
    .Where(dv => dv.AdmissionId == admissionId && dv.IsActive == Status.Active)
    .OrderByDescending(dv => dv.VisitDate)
    .ToListAsync();

            return View(admission);
        }




        // ==================================================================
        //  RECORD DOCTOR VISIT (GET)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> RecordVisit(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value);

            if (admission == null) return NotFound();

            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            ViewBag.AdmissionId = admissionId;

            return View(new DoctorVisit
            {
                AdmissionId = admissionId,
                VisitDate = DateTime.Now,
                DoctorId = doctorId.Value,
                IsContactRecord = false
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordVisit(DoctorVisit visit)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            visit.DoctorId = doctorId.Value;
            visit.IsContactRecord = false;

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Doctor");
            ModelState.Remove("ExternalDoctorName");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == visit.AdmissionId && a.DoctorId == doctorId.Value);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = visit.AdmissionId;
                return View(visit);
            }

            var valid = await _context.Admissions
                .AnyAsync(a => a.Id == visit.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (!valid)
            {
                ModelState.AddModelError("", "You are not authorised to record a visit for this patient.");
                return View(visit);
            }

            visit.IsActive = Status.Active;
            visit.StartVisitTime = DateTime.Now;   // add this line before saving
            _context.DoctorVisits.Add(visit);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit recorded successfully.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        [HttpGet]
        public async Task<IActionResult> EditVisit(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);

            if (visit == null) return NotFound();

            ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
            return View(visit);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditVisit(int id, DoctorVisit posted)
        {
            if (id != posted.Id) return BadRequest();
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var existing = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);
            if (existing == null) return NotFound();

            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Doctor");
            ModelState.Remove("ExternalDoctorName");

            if (!ModelState.IsValid)
            {
                var visit = await _context.DoctorVisits
                    .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(dv => dv.Id == id);
                if (visit != null)
                    ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
                return View(posted);
            }

            existing.VisitDate = posted.VisitDate;
            existing.Instructions = posted.Instructions;
            existing.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit updated.";
            return RedirectToAction("PatientFolder", new { admissionId = existing.AdmissionId });
        }

        [HttpPost]
        public async Task<IActionResult> VisitDetails(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .Include(dv => dv.Doctor)
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value);

            if (visit == null) return NotFound();
            return View(visit);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteVisit(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value);
            if (visit == null) return NotFound();

            visit.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit deactivated.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreVisit(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .FirstOrDefaultAsync(dv => dv.Id == id && dv.DoctorId == doctorId.Value);
            if (visit == null) return NotFound();

            visit.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Visit reactivated.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        [HttpPost]
        public async Task<IActionResult> TreatmentsByAdmission(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = (await _context.Patients.FindAsync(admission.PatientId))?.FullName;

            var treatments = await _context.Treatments
                .Where(t => t.AdmissionId == admissionId && t.IsActive == Status.Active)
                .OrderByDescending(t => t.TreatmentDate)
                .ToListAsync();
            return View(treatments);
        }

        [HttpGet]
        public async Task<IActionResult> RecordTreatment(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            return View(new Treatment { AdmissionId = admissionId, TreatmentDate = DateTime.Now });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordTreatment(Treatment treatment)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == treatment.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = treatment.AdmissionId;
                return View(treatment);
            }

            var valid = await _context.Admissions
                .AnyAsync(a => a.Id == treatment.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (!valid) return BadRequest("You are not authorised to record a treatment for this patient.");

            treatment.IsActive = Status.Active;
            _context.Treatments.Add(treatment);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment recorded.";
            return RedirectToAction("TreatmentsByAdmission", new { admissionId = treatment.AdmissionId });
        }

        [HttpGet]
        public async Task<IActionResult> EditTreatment(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive == Status.Active && t.Admission.DoctorId == doctorId.Value);
            if (treatment == null) return NotFound();

            ViewBag.PatientName = (await _context.Patients.FindAsync(treatment.Admission.PatientId))?.FullName;
            return View(treatment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTreatment(int id, Treatment posted)
        {
            if (id != posted.Id) return BadRequest();
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var existing = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive == Status.Active && t.Admission.DoctorId == doctorId.Value);
            if (existing == null) return NotFound();

            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");

            if (!ModelState.IsValid)
            {
                ViewBag.PatientName = (await _context.Patients.FindAsync(existing.Admission.PatientId))?.FullName;
                return View(posted);
            }

            existing.TreatmentType = posted.TreatmentType;
            existing.Notes = posted.Notes;
            existing.TreatmentDate = posted.TreatmentDate;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment updated.";
            return RedirectToAction("TreatmentsByAdmission", new { admissionId = existing.AdmissionId });
        }

        [HttpPost]
        public async Task<IActionResult> TreatmentDetails(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.DoctorId == doctorId.Value);
            if (treatment == null) return NotFound();
            return View(treatment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTreatment(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.DoctorId == doctorId.Value);
            if (treatment == null) return NotFound();

            treatment.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment deactivated.";
            return RedirectToAction("TreatmentsByAdmission", new { admissionId = treatment.AdmissionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreTreatment(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.DoctorId == doctorId.Value);
            if (treatment == null) return NotFound();

            treatment.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment reactivated.";
            return RedirectToAction("TreatmentsByAdmission", new { admissionId = treatment.AdmissionId });
        }

        [HttpGet]
        public async Task<IActionResult> ScheduleVisit(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);

            if (admission == null) return NotFound();

            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            ViewBag.AdmissionId = admissionId;

            return View(new DoctorVisit
            {
                AdmissionId = admissionId,
                VisitDate = DateTime.Now.AddHours(1),
                DoctorId = doctorId.Value,
                IsContactRecord = false
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScheduleVisit(DoctorVisit visit)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            visit.DoctorId = doctorId.Value;
            visit.IsContactRecord = false;
            visit.Instructions = null;
            visit.Notes = null;

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Doctor");
            ModelState.Remove("ExternalDoctorName");
            ModelState.Remove("Instructions");
            ModelState.Remove("Notes");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == visit.AdmissionId && a.DoctorId == doctorId.Value);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = visit.AdmissionId;
                return View(visit);
            }

            var valid = await _context.Admissions
                .AnyAsync(a => a.Id == visit.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (!valid)
            {
                ModelState.AddModelError("", "You are not authorised to schedule a visit for this patient.");
                return View(visit);
            }

            visit.IsActive = Status.Active;
            _context.DoctorVisits.Add(visit);
            await _context.SaveChangesAsync();

            try
            {
                string doctorName = (await _context.Employees.FindAsync(doctorId))?.FullName ?? "Doctor";
                var admission = await _context.Admissions.Include(a => a.Patient).FirstOrDefaultAsync(a => a.Id == visit.AdmissionId);
                if (admission?.PatientId != null)
                {
                    string patientName = admission.Patient.FullName;
                    string patientLink = Url.Action("MyPatientFolder", "Patient", new { admissionId = visit.AdmissionId });
                    await _notifService.NotifyUserAsync(
                        admission.PatientId,
                        "Patient",
                        $"{doctorName} has scheduled a visit for you on {visit.VisitDate:ddd, dd MMM yyyy HH:mm}.",
                        patientLink);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Visit scheduled.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        [HttpGet]
        public async Task<IActionResult> MyScheduledVisits()
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var upcoming = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .Include(dv => dv.Admission.Bed).ThenInclude(b => b.Ward)
                .Where(dv => dv.DoctorId == doctorId.Value
                             && dv.IsActive == Status.Active
                             && dv.VisitDate > DateTime.Now)
                .OrderBy(dv => dv.VisitDate)
                .ToListAsync();

            return View(upcoming);
        }

        [HttpGet]
        public async Task<IActionResult> WriteInstructions(int visitId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(dv => dv.Id == visitId && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);

            if (visit == null) return NotFound();

            ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
            ViewBag.VisitId = visitId;
            ViewBag.ExistingInstructions = visit.Instructions;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WriteInstructions(int visitId, string instructions)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var visit = await _context.DoctorVisits
                .Include(dv => dv.Admission)
                    .ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(dv => dv.Id == visitId && dv.DoctorId == doctorId.Value && dv.IsActive == Status.Active);
            if (visit == null) return NotFound();

            if (string.IsNullOrWhiteSpace(instructions))
            {
                ModelState.AddModelError("", "Instructions cannot be empty.");
                ViewBag.PatientName = visit.Admission.Patient.FullName;
                ViewBag.VisitId = visitId;
                ViewBag.ExistingInstructions = visit.Instructions;
                return View();
            }

            visit.Instructions = instructions;
            await _context.SaveChangesAsync();

            try
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == visit.AdmissionId);

                if (admission?.NurseId != null)
                {
                    string doctorName = (await _context.Employees.FindAsync(doctorId))?.FullName ?? "The doctor";
                    string patientName = admission.Patient.FullName;
                    string nurseLink = Url.Action("DoctorVisitsByAdmission", "Nurse", new { admissionId = visit.AdmissionId });

                    await _notifService.NotifyUserAsync(
                        admission.NurseId.Value,
                        "Employee",
                        $"{doctorName} has written new instructions for patient {patientName}.",
                        nurseLink);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Instructions saved.";
            return RedirectToAction("PatientFolder", new { admissionId = visit.AdmissionId });
        }

        [HttpPost]
        public async Task<IActionResult> PatientInstructions(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            var patient = await _context.Patients.FindAsync(admission.PatientId);
            ViewBag.PatientName = patient?.FullName;

            var instructionsList = await _context.DoctorVisits
                .Where(dv => dv.AdmissionId == admissionId
                             && dv.IsActive == Status.Active
                             && !string.IsNullOrEmpty(dv.Instructions))
                .OrderByDescending(dv => dv.VisitDate)
                .Select(dv => new PatientInstructionViewModel
                {
                    VisitDate = dv.VisitDate,
                    Instructions = dv.Instructions,
                    DoctorName = dv.Doctor != null ? dv.Doctor.FullName : "Unknown"
                })
                .ToListAsync();

            return View(instructionsList);
        }

        [HttpGet]
        public async Task<IActionResult> PrescriptionsByAdmission(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = (await _context.Patients.FindAsync(admission.PatientId))?.FullName;

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Medication)
                .Where(p => p.AdmissionId == admissionId && p.IsActive == Status.Active)
                .OrderByDescending(p => p.PrescribedDate)
                .ToListAsync();

            return View(prescriptions);
        }

        [HttpGet]
        public async Task<IActionResult> PrescribeMedication(int admissionId)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            ViewBag.AdmissionId = admissionId;

            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active)
                    .OrderBy(m => m.Name)
                    .ToListAsync(),
                "Id", "Name");

            ViewBag.ScriptManagers = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.SCRIPTMANAGER && e.IsActive == Status.Active)
                    .OrderBy(e => e.LastName)
                    .ToListAsync(),
                "EmployeeID", "FullName");

            return View(new Prescription
            {
                AdmissionId = admissionId,
                PrescribedDate = DateTime.Now
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrescribeMedication(Prescription prescription, bool overrideDuplicate = false)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            // Basic validation
            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");
            ModelState.Remove("ScriptManager");

            if (!ModelState.IsValid)
            {
                await PopulatePrescriptionDropdowns(prescription.AdmissionId, prescription.MedicationId, prescription.ScriptManagerId);
                return View(prescription);
            }

            // Authorisation
            var admission = await _context.Admissions
                .FirstOrDefaultAsync(a => a.Id == prescription.AdmissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);

            if (admission == null)
                return BadRequest("You are not authorised to prescribe for this patient.");

            // Medication
            var medication = await _context.Medications
                .FirstOrDefaultAsync(m => m.Id == prescription.MedicationId && m.IsActive == Status.Active);

            if (medication == null)
            {
                ModelState.AddModelError("MedicationId", "Selected medication is invalid.");
                await PopulatePrescriptionDropdowns(prescription.AdmissionId, prescription.MedicationId, prescription.ScriptManagerId);
                return View(prescription);
            }

            // ========== ALLERGY CHECK ==========
            bool hasAllergy = await _context.AdmissionAllergies
                .Include(aa => aa.Allergy)
                .AnyAsync(aa => aa.AdmissionId == prescription.AdmissionId
                              && aa.Allergy.IsActive == Status.Active
                              && aa.Allergy.Name.Equals(medication.Name, StringComparison.OrdinalIgnoreCase));

            if (hasAllergy)
            {
                ModelState.AddModelError("MedicationId", $"Patient is allergic to {medication.Name}. This medication cannot be prescribed.");
                await PopulatePrescriptionDropdowns(prescription.AdmissionId, prescription.MedicationId, prescription.ScriptManagerId);
                return View(prescription);
            }

            // ========== DRUG INTERACTION CHECK ==========
            var (isHighRisk, interactingDrug) = await CheckDrugInteractionAsync(prescription.AdmissionId, medication.Name);
            if (isHighRisk)
            {
                ModelState.AddModelError("MedicationId", $"High‑risk interaction with {interactingDrug}. This medication cannot be prescribed.");
                await PopulatePrescriptionDropdowns(prescription.AdmissionId, prescription.MedicationId, prescription.ScriptManagerId);
                return View(prescription);
            }

            // ========== DUPLICATE PRESCRIPTION DETECTION ==========
            bool isDuplicate = await _context.Prescriptions
                .AnyAsync(p => p.AdmissionId == prescription.AdmissionId
                             && p.MedicationId == prescription.MedicationId
                             && p.Dosage == prescription.Dosage
                             && p.Frequency == prescription.Frequency
                             && p.IsActive == Status.Active
                             && p.ScriptStatus != ScriptStatus.Rejected); // optional: ignore rejected

            if (isDuplicate && !overrideDuplicate)
            {
                ModelState.AddModelError("MedicationId", $"Duplicate prescription detected: {medication.Name} {prescription.Dosage}, {prescription.Frequency}. Check 'I want to continue' to override.");
                await PopulatePrescriptionDropdowns(prescription.AdmissionId, prescription.MedicationId, prescription.ScriptManagerId);
                return View(prescription);
            }
            // =====================================================

            // Save
            prescription.IsActive = Status.Active;
            prescription.ScriptStatus = ScriptStatus.New;
            _context.Prescriptions.Add(prescription);
            await _context.SaveChangesAsync();

            // Notifications (existing)
            try
            {
                string doctorName = (await _context.Employees.FindAsync(doctorId))?.FullName ?? "Doctor";
                string patientName = admission.Patient?.FullName ?? "a patient";
                string medName = medication.Name;
                string patientLink = Url.Action("MyInstructions", "Patient");

                if (prescription.ScriptManagerId.HasValue)
                {
                    string scriptLink = Url.Action("NewScripts", "ScriptManager");
                    await _notifService.NotifyUserAsync(
                        prescription.ScriptManagerId.Value,
                        "Employee",
                        $"{doctorName} assigned you a new prescription for {patientName}: {medName}.",
                        scriptLink);
                }

                int? patientUserId = admission.PatientId;
                if (patientUserId.HasValue)
                {
                    await _notifService.NotifyUserAsync(
                        patientUserId.Value,
                        "Patient",
                        $"{doctorName} has prescribed {medName} for you.",
                        patientLink);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Medication prescribed.";
            return RedirectToAction("PrescriptionsByAdmission", new { admissionId = prescription.AdmissionId });
        }




        // Private helper to repopulate dropdowns (to avoid code duplication)
        private async Task PopulatePrescriptionDropdowns(int admissionId, int? medicationId = null, int? scriptManagerId = null)
        {
            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId);

            if (admission != null)
                ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            ViewBag.AdmissionId = admissionId;

            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active)
                    .OrderBy(m => m.Name).ToListAsync(),
                "Id", "Name", medicationId);

            ViewBag.ScriptManagers = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.SCRIPTMANAGER && e.IsActive == Status.Active)
                    .OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName", scriptManagerId);
        }

        [HttpGet]
        public async Task<IActionResult> EditPrescription(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value && p.IsActive == Status.Active);
            if (prescription == null) return NotFound();

            ViewBag.PatientName = $"{prescription.Admission.Patient.FirstName} {prescription.Admission.Patient.LastName}";
            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active)
                    .OrderBy(m => m.Name).ToListAsync(),
                "Id", "Name", prescription.MedicationId);

            return View(prescription);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPrescription(int id, Prescription posted)
        {
            if (id != posted.Id) return BadRequest();
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var existing = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value && p.IsActive == Status.Active);
            if (existing == null) return NotFound();

            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");

            if (!ModelState.IsValid)
            {
                var prescription = await _context.Prescriptions
                    .Include(p => p.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(p => p.Id == id);
                if (prescription != null)
                    ViewBag.PatientName = $"{prescription.Admission.Patient.FirstName} {prescription.Admission.Patient.LastName}";
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active)
                        .OrderBy(m => m.Name).ToListAsync(),
                    "Id", "Name", posted.MedicationId);
                return View(posted);
            }

            existing.MedicationId = posted.MedicationId;
            existing.Dosage = posted.Dosage;
            existing.Frequency = posted.Frequency;
            existing.Duration = posted.Duration;
            existing.Notes = posted.Notes;
            existing.PrescribedDate = posted.PrescribedDate;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription updated.";
            return RedirectToAction("PrescriptionsByAdmission", new { admissionId = existing.AdmissionId });
        }

        [HttpGet]
        public async Task<IActionResult> PrescriptionDetails(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .Include(p => p.Admission).ThenInclude(a => a.Patient)
                .Include(p => p.Medication)
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value);
            if (prescription == null) return NotFound();
            return View(prescription);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePrescription(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value);
            if (prescription == null) return NotFound();

            prescription.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription deactivated.";
            return RedirectToAction("PrescriptionsByAdmission", new { admissionId = prescription.AdmissionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestorePrescription(int id)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var prescription = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == id && p.Admission.DoctorId == doctorId.Value);
            if (prescription == null) return NotFound();

            prescription.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prescription reactivated.";
            return RedirectToAction("PrescriptionsByAdmission", new { admissionId = prescription.AdmissionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DischargePatient(int admissionId, string? dischargeInstructions)
        {
            var doctorId = GetCurrentDoctorId();
            if (doctorId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Bed)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.DoctorId == doctorId.Value && a.IsActive == Status.Active);

            if (admission == null) return NotFound();

            admission.IsActive = Status.Inactive;
            admission.DischargeDate = DateTime.Now;

            if (admission.Bed != null)
                admission.Bed.IsOccupied = false;

            if (!string.IsNullOrWhiteSpace(dischargeInstructions))
            {
                var dischargeVisit = new DoctorVisit
                {
                    AdmissionId = admissionId,
                    DoctorId = doctorId.Value,
                    VisitDate = DateTime.Now,
                    Instructions = dischargeInstructions,
                    Notes = "Discharge ordered by doctor",
                    IsContactRecord = false,
                    IsActive = Status.Active
                };
                _context.DoctorVisits.Add(dischargeVisit);
            }

            await _context.SaveChangesAsync();

            try
            {
                var admissionWithAdmin = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == admissionId);

                if (admissionWithAdmin?.CreatedByWardAdminId != null)
                {
                    string doctorName = (await _context.Employees.FindAsync(doctorId))?.FullName ?? "Doctor";
                    string patientName = admissionWithAdmin.Patient.FullName;
                    string notificationMsg = $"{doctorName} has initiated discharge for patient {patientName}.";
                    if (!string.IsNullOrWhiteSpace(dischargeInstructions))
                        notificationMsg += $" Instructions: {dischargeInstructions}";

                    string link = Url.Action("Details", "WardAdmin", new { id = admissionId });

                    await _notifService.NotifyUserAsync(
                        admissionWithAdmin.CreatedByWardAdminId.Value,
                        "Employee",
                        notificationMsg,
                        link);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Patient discharged successfully.";
            return RedirectToAction("MyPatients");
        }

        // ------------------------- PRIVATE HELPERS -------------------------
        private async Task<List<int>> GetAdmissionIdsWithCriticalVitalsAsync(int doctorId)
        {
            var activeAdmissions = await _context.Admissions
                .Where(a => a.DoctorId == doctorId && a.IsActive == Status.Active)
                .Select(a => a.Id)
                .ToListAsync();

            var criticalIds = new List<int>();

            foreach (var admId in activeAdmissions)
            {
                var latest = await _context.Vitals
                    .Where(v => v.AdmissionId == admId && v.IsActive == Status.Active)
                    .OrderByDescending(v => v.DateRecorded)
                    .FirstOrDefaultAsync();

                if (latest == null) continue;

                bool critical = false;
                if (!string.IsNullOrEmpty(latest.BloodPressure))
                {
                    var parts = latest.BloodPressure.Split('/');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int sys) && int.TryParse(parts[1], out int dia))
                    {
                        if (sys > 180 || dia > 120) critical = true;
                    }
                }
                if (latest.TemperatureCelsius.HasValue && latest.TemperatureCelsius > 39.0m) critical = true;
                if (latest.HeartRateBpm.HasValue && latest.HeartRateBpm > 120) critical = true;
                if (latest.OxygenSaturation.HasValue && latest.OxygenSaturation < 90) critical = true;

                if (critical) criticalIds.Add(admId);
            }

            return criticalIds;
        }

        private async Task<List<int>> GetAdmissionIdsWithAbnormalVitalsAsync(int doctorId)
        {
            var activeAdmissions = await _context.Admissions
                .Where(a => a.DoctorId == doctorId && a.IsActive == Status.Active)
                .Select(a => a.Id)
                .ToListAsync();

            var abnormalIds = new List<int>();

            foreach (var admId in activeAdmissions)
            {
                var latest = await _context.Vitals
                    .Where(v => v.AdmissionId == admId && v.IsActive == Status.Active)
                    .OrderByDescending(v => v.DateRecorded)
                    .FirstOrDefaultAsync();

                if (latest == null) continue;

                bool abnormal = false;
                if (!string.IsNullOrEmpty(latest.BloodPressure))
                {
                    var parts = latest.BloodPressure.Split('/');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int sys) && int.TryParse(parts[1], out int dia))
                    {
                        if (sys > 140 || sys < 90 || dia > 90 || dia < 60) abnormal = true;
                    }
                }
                if (latest.TemperatureCelsius.HasValue && (latest.TemperatureCelsius > 37.5m || latest.TemperatureCelsius < 36.0m)) abnormal = true;
                if (latest.HeartRateBpm.HasValue && (latest.HeartRateBpm > 100 || latest.HeartRateBpm < 60)) abnormal = true;
                if (latest.OxygenSaturation.HasValue && latest.OxygenSaturation < 95) abnormal = true;

                if (abnormal) abnormalIds.Add(admId);
            }

            return abnormalIds;
        }

        // ----- Drug Interaction Checker -----
        private static readonly Dictionary<(string Drug1, string Drug2), string> DrugInteractions = new()
{
    // format: (DrugA, DrugB) -> RiskLevel (High/Medium/Low)
    // Drug names are compared case‑insensitively.
    { ("Warfarin", "Ibuprofen"), "High" },
    { ("Warfarin", "Aspirin"), "High" },
    { ("Warfarin", "Naproxen"), "High" },
    { ("Warfarin", "Clopidogrel"), "High" },
    { ("Insulin", "Metformin"), "Low" },
    { ("Insulin", "Aspirin"), "Medium" },
    { ("Metformin", "Aspirin"), "Medium" },
    { ("Metformin", "Ibuprofen"), "Medium" },
    { ("ACE Inhibitor", "Potassium Chloride"), "High" },
    { ("ACE Inhibitor", "Spironolactone"), "High" },
    { ("Simvastatin", "Warfarin"), "High" },
    { ("Simvastatin", "Amiodarone"), "High" },
    { ("Digoxin", "Amiodarone"), "High" },
    { ("Digoxin", "Verapamil"), "High" },
    { ("Theophylline", "Ciprofloxacin"), "High" },
    { ("Methotrexate", "Ibuprofen"), "High" },
    { ("Lithium", "Ibuprofen"), "High" },
    { ("Lithium", "ACE Inhibitor"), "High" },
    { ("Ciprofloxacin", "Warfarin"), "High" },
    // ... add more as needed
};

        private async Task<(bool IsHighRisk, string InteractingDrug)> CheckDrugInteractionAsync(
     int admissionId, string newMedicationName)
        {
            // Retrieve current medications (from AdmissionMedications table)
            // NOTE: AdmissionMedication has no IsActive field, so we take all records for this admission.
            var currentMeds = await _context.AdmissionMedications
                .Include(am => am.Medication)
                .Where(am => am.AdmissionId == admissionId)
                .Select(am => am.Medication.Name)
                .ToListAsync();

            // Also check active prescriptions (active and not rejected as a proxy for "discontinued")
            var activePrescriptions = await _context.Prescriptions
                .Include(p => p.Medication)
                .Where(p => p.AdmissionId == admissionId
                           && p.IsActive == Status.Active)
                .Select(p => p.Medication.Name)
                .ToListAsync();

            // Combine both lists (unique, case‑insensitive)
            var allCurrentMeds = currentMeds.Union(activePrescriptions, StringComparer.OrdinalIgnoreCase)
                                            .Distinct(StringComparer.OrdinalIgnoreCase)
                                            .ToList();

            foreach (var currentMed in allCurrentMeds)
            {
                var pair = new SortedSet<string>(new[] { newMedicationName, currentMed }, StringComparer.OrdinalIgnoreCase);
                if (pair.Count == 2)
                {
                    var ordered = pair.ToArray();
                    var key = (ordered[0], ordered[1]);
                    if (DrugInteractions.TryGetValue(key, out string? risk) && risk == "High")
                    {
                        return (true, currentMed);
                    }
                }
            }

            return (false, string.Empty);
        }


    }
}