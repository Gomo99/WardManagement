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
    [Authorize(Roles = "NURSE")]
    [Route("[controller]")]

    public class NurseController : Controller
    {
        private readonly WardDbContext _context;
        private readonly INotificationService _notifService;   // <-- new

        public NurseController(WardDbContext context, INotificationService notifService)
        {
            _context = context;
            _notifService = notifService;
        }

        // ---------------------------------------------------------------
        //  DASHBOARD
        // ---------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");
            ViewBag.ActivePatients = await _context.Admissions.CountAsync(a => a.IsActive == Status.Active);
            ViewBag.PendingVitals = await _context.Vitals.CountAsync(v => v.DateRecorded.Date == DateTime.Today && v.IsActive == Status.Active);
            return View();
        }


        private int? GetCurrentNurseId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            // Verify that the user is actually a nurse
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.NURSE.ToString())
                return null;
            return id;
        }


        // ===============================================================
        //  VIEW ADMITTED PATIENTS
        // ===============================================================

        [HttpGet("Patients")]
        public async Task<IActionResult> Patients()
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admissions = await _context.Admissions
                .Include(a => a.Patient)
                .Include(a => a.Bed).ThenInclude(b => b.Ward)
                .Where(a => a.NurseId == nurseId.Value && a.IsActive == Status.Active)
                .OrderBy(a => a.Patient.LastName)
                .ToListAsync();
            return View(admissions);
        }

        // ===============================================================
        //  VITALS (unchanged)
        // ===============================================================
        [HttpGet("VitalsByAdmission/{int:id}")]

        public async Task<IActionResult> VitalsByAdmission(int admissionId)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            var vitals = await _context.Vitals
                .Where(v => v.AdmissionId == admissionId && v.IsActive == Status.Active)
                .OrderByDescending(v => v.DateRecorded)
                .ToListAsync();
            return View(vitals);
        }

        [HttpGet("RecordVital/{int:id}")]
        public async Task<IActionResult> RecordVital(int admissionId)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            return View(new Vitals { AdmissionId = admissionId, DateRecorded = DateTime.Now });
        }

        [HttpPost("RecordVital")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordVital(Vitals vitals)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == vitals.AdmissionId && a.IsActive == Status.Active);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = vitals.AdmissionId;
                return View(vitals);
            }

            var validAdmission = await _context.Admissions
                .AnyAsync(a => a.Id == vitals.AdmissionId && a.IsActive == Status.Active);
            if (!validAdmission)
            {
                ModelState.AddModelError("", "Invalid admission.");
                return View(vitals);
            }

            vitals.IsActive = Status.Active;
            _context.Vitals.Add(vitals);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Vitals recorded.";
            return RedirectToAction(nameof(VitalsByAdmission), new { admissionId = vitals.AdmissionId });
        }

        [HttpGet("EditVital/{int:id}")]
        public async Task<IActionResult> EditVital(int id)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var vital = await _context.Vitals
                .Include(v => v.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(v => v.Id == id && v.IsActive == Status.Active && v.Admission.NurseId == nurseId.Value);
            if (vital == null) return NotFound();

            ViewBag.PatientName = $"{vital.Admission.Patient.FirstName} {vital.Admission.Patient.LastName}";
            return View(vital);
        }

        [HttpPost("EditVital/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditVital(int id, Vitals posted)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");

            if (!ModelState.IsValid)
            {
                var vital = await _context.Vitals
                    .Include(v => v.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(v => v.Id == id && v.Admission.NurseId == nurseId.Value);
                if (vital != null)
                    ViewBag.PatientName = $"{vital.Admission.Patient.FirstName} {vital.Admission.Patient.LastName}";
                return View(posted);
            }

            var existing = await _context.Vitals.FindAsync(id);
            if (existing == null || existing.IsActive != Status.Active) return NotFound();

            existing.DateRecorded = posted.DateRecorded;
            existing.BloodPressure = posted.BloodPressure;
            existing.TemperatureCelsius = posted.TemperatureCelsius;
            existing.BloodSugarMmolL = posted.BloodSugarMmolL;
            existing.HeartRateBpm = posted.HeartRateBpm;
            existing.RespiratoryRate = posted.RespiratoryRate;
            existing.OxygenSaturation = posted.OxygenSaturation;
            existing.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Vitals updated.";
            return RedirectToAction(nameof(VitalsByAdmission), new { admissionId = existing.AdmissionId });
        }

        [HttpGet("VitalDetails/{int:id}")]

        public async Task<IActionResult> VitalDetails(int id)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var vital = await _context.Vitals
                .Include(v => v.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(v => v.Id == id && v.Admission.NurseId == nurseId.Value && v.IsActive == Status.Active);
            if (vital == null) return NotFound();
            return View(vital);
        }

        [HttpPost("DeleteVital/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteVital(int id)
        {
             int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var vital = await _context.Vitals.FindAsync(id);
            if (vital == null) return NotFound();

            vital.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Vital record deactivated.";
            return RedirectToAction(nameof(VitalsByAdmission), new { admissionId = vital.AdmissionId });
        }

        // ===============================================================
        //  TREATMENTS (unchanged)
        // ===============================================================
        [HttpGet("TreatmentsByAdmission/{int:id}")]

        public async Task<IActionResult> TreatmentsByAdmission(int admissionId)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            var treatments = await _context.Treatments
                .Where(t => t.AdmissionId == admissionId && t.IsActive == Status.Active)
                .OrderByDescending(t => t.TreatmentDate)
                .ToListAsync();
            return View(treatments);
        }

        [HttpGet("RecordTreatment/{int:id}")]

        public async Task<IActionResult> RecordTreatment(int admissionId)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
            return View(new Treatment { AdmissionId = admissionId, TreatmentDate = DateTime.Now });
        }

        [HttpPost("RecordTreatment")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordTreatment(Treatment treatment)
        {

            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == treatment.AdmissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = treatment.AdmissionId;
                return View(treatment);
            }

            var valid = await _context.Admissions
                .AnyAsync(a => a.Id == treatment.AdmissionId && a.IsActive == Status.Active);
            if (!valid)
                return BadRequest("Invalid admission.");

            treatment.IsActive = Status.Active;
            _context.Treatments.Add(treatment);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment recorded.";
            return RedirectToAction(nameof(TreatmentsByAdmission), new { admissionId = treatment.AdmissionId });
        }

        [HttpGet("EditTreatment/{int:id}")]
        public async Task<IActionResult> EditTreatment(int id)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsActive == Status.Active && t.Admission.NurseId == nurseId.Value);
            if (treatment == null) return NotFound();

            ViewBag.PatientName = $"{treatment.Admission.Patient.FirstName} {treatment.Admission.Patient.LastName}";
            return View(treatment);
        }

        [HttpPost("EditTreatment/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTreatment(int id, Treatment posted)
        {

            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");

            if (!ModelState.IsValid)
            {
                var treatment = await _context.Treatments
                    .Include(t => t.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(t => t.Id == id && t.Admission.NurseId == nurseId.Value);
                if (treatment != null)
                    ViewBag.PatientName = $"{treatment.Admission.Patient.FirstName} {treatment.Admission.Patient.LastName}";
                return View(posted);
            }

            var existing = await _context.Treatments.FindAsync(id);
            if (existing == null || existing.IsActive != Status.Active) return NotFound();

            existing.TreatmentType = posted.TreatmentType;
            existing.Notes = posted.Notes;
            existing.TreatmentDate = posted.TreatmentDate;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment updated.";
            return RedirectToAction(nameof(TreatmentsByAdmission), new { admissionId = existing.AdmissionId });
        }


        [HttpGet("TreatmentDetails/{int:id}")]
        public async Task<IActionResult> TreatmentDetails(int id)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.NurseId == nurseId.Value);
            if (treatment == null) return NotFound();
            return View(treatment);
        }

        [HttpPost("DeleteTreatment/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTreatment(int id)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.NurseId == nurseId.Value);
            if (treatment == null) return NotFound();

            treatment.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment deactivated.";
            return RedirectToAction(nameof(TreatmentsByAdmission), new { admissionId = treatment.AdmissionId });
        }

        [HttpPost("RestoreTreatment/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreTreatment(int id)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var treatment = await _context.Treatments
                .Include(t => t.Admission)
                .FirstOrDefaultAsync(t => t.Id == id && t.Admission.NurseId == nurseId.Value);
            if (treatment == null) return NotFound();

            treatment.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Treatment reactivated.";
            return RedirectToAction(nameof(TreatmentsByAdmission), new { admissionId = treatment.AdmissionId });
        }

        // ===============================================================
        //  MEDICATION ADMINISTRATION (unchanged)
        // ===============================================================

        [HttpGet("MedicationAdministrationsByAdmission/{int:id}")]
        public async Task<IActionResult> MedicationAdministrationsByAdmission(int admissionId)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            var administrations = await _context.MedicationAdministrations
                .Include(ma => ma.Medication)
                .Where(ma => ma.AdmissionId == admissionId && ma.IsActive == Status.Active)
                .OrderByDescending(ma => ma.DateAdministered)
                .ToListAsync();
            return View(administrations);
        }

        [HttpGet("AdministerMedication/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdministerMedication(int admissionId)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active && (!m.Schedule.HasValue || m.Schedule <= 4))
                    .OrderBy(m => m.Name)
                    .ToListAsync(),
                "Id", "Name");

            return View(new MedicationAdministration
            {
                AdmissionId = admissionId,
                DateAdministered = DateTime.Now
            });
        }

        [HttpPost("AdministerMedication/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdministerMedication(MedicationAdministration administration)
        {
            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");



                        int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");


            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == administration.AdmissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);

            if (!ModelState.IsValid)
            {
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = administration.AdmissionId;
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active && (!m.Schedule.HasValue || m.Schedule <= 4))
                        .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", administration.MedicationId);
                return View(administration);
            }

            if (admission == null)
            {
                ModelState.AddModelError("", "Invalid admission.");
                return View(administration);
            }

            var medication = await _context.Medications.FindAsync(administration.MedicationId);
            if (medication == null || (medication.Schedule.HasValue && medication.Schedule >= 5))
            {
                ModelState.AddModelError("", "You are not authorized to administer this medication. Only Nursing Sister can handle schedule 5+.");
                ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = administration.AdmissionId;
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active && (!m.Schedule.HasValue || m.Schedule <= 4))
                        .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", administration.MedicationId);
                return View(administration);
            }

            administration.IsActive = Status.Active;
            _context.MedicationAdministrations.Add(administration);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Medication administered.";
            return RedirectToAction(nameof(MedicationAdministrationsByAdmission), new { admissionId = administration.AdmissionId });
        }

        [HttpGet("EditMedicationAdministration/{int:id}")]
        public async Task<IActionResult> EditMedicationAdministration(int id)
        {

            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var administration = await _context.MedicationAdministrations
                .Include(ma => ma.Admission).ThenInclude(a => a.Patient)
                .Include(ma => ma.Medication)
                .FirstOrDefaultAsync(ma => ma.Id == id && ma.IsActive == Status.Active && ma.Admission.NurseId == nurseId.Value);
            if (administration == null) return NotFound();

            ViewBag.PatientName = $"{administration.Admission.Patient.FirstName} {administration.Admission.Patient.LastName}";
            ViewBag.Medications = new SelectList(
                await _context.Medications
                    .Where(m => m.IsActive == Status.Active && (!m.Schedule.HasValue || m.Schedule <= 4))
                    .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", administration.MedicationId);
            return View(administration);
        }

        [HttpPost("EditMedicationAdministration/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMedicationAdministration(int id, MedicationAdministration posted)
        {
            if (id != posted.Id) return BadRequest();
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Medication");


            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");




            var existing = await _context.MedicationAdministrations
                .Include(ma => ma.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(ma => ma.Id == id && ma.IsActive == Status.Active && ma.Admission.NurseId == nurseId.Value);
            if (existing == null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.PatientName = $"{existing.Admission.Patient.FirstName} {existing.Admission.Patient.LastName}";
                ViewBag.Medications = new SelectList(
                    await _context.Medications
                        .Where(m => m.IsActive == Status.Active && (!m.Schedule.HasValue || m.Schedule <= 4))
                        .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", posted.MedicationId);
                return View(posted);
            }

            if (posted.MedicationId != existing.MedicationId)
            {
                var newMed = await _context.Medications.FindAsync(posted.MedicationId);
                if (newMed == null || (newMed.Schedule.HasValue && newMed.Schedule >= 5))
                {
                    ModelState.AddModelError("", "You are not allowed to administer this medication.");
                    ViewBag.PatientName = $"{existing.Admission.Patient.FirstName} {existing.Admission.Patient.LastName}";
                    ViewBag.Medications = new SelectList(
                        await _context.Medications
                            .Where(m => m.IsActive == Status.Active && (!m.Schedule.HasValue || m.Schedule <= 4))
                            .OrderBy(m => m.Name).ToListAsync(), "Id", "Name", posted.MedicationId);
                    return View(posted);
                }
            }

            existing.MedicationId = posted.MedicationId;
            existing.Dosage = posted.Dosage;
            existing.DateAdministered = posted.DateAdministered;
            existing.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Administration record updated.";
            return RedirectToAction(nameof(MedicationAdministrationsByAdmission), new { admissionId = existing.AdmissionId });
        }

        [HttpPost("DeleteMedicationAdministration/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMedicationAdministration(int id)
        {


            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");


            var administration = await _context.MedicationAdministrations.FindAsync(id);
            if (administration == null) return NotFound();

            administration.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Administration record deactivated.";
            return RedirectToAction(nameof(MedicationAdministrationsByAdmission), new { admissionId = administration.AdmissionId });
        }

        [HttpPost("RestoreMedicationAdministration/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreMedicationAdministration(int id)
        {

            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var administration = await _context.MedicationAdministrations.FindAsync(id);
            if (administration == null) return NotFound();

            administration.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Administration record reactivated.";
            return RedirectToAction(nameof(MedicationAdministrationsByAdmission), new { admissionId = administration.AdmissionId });
        }

        // ===============================================================
        //  DOCTOR VISITS / INSTRUCTIONS
        //  (View instructions from doctor visits + record phone advice)
        // ===============================================================

        [HttpGet("DoctorVisitsByAdmission/{int:id}")]
        public async Task<IActionResult> DoctorVisitsByAdmission(int admissionId)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            var visits = await _context.DoctorVisits
                .Include(v => v.Doctor)
                .Where(v => v.AdmissionId == admissionId && v.IsActive == Status.Active)
                .OrderByDescending(v => v.VisitDate)
                .ToListAsync();

            return View(visits);
        }

        // Record a phone contact / advice received from a doctor
        [HttpGet("RecordDoctorContact/{int:id}")]
        public async Task<IActionResult> RecordDoctorContact(int admissionId)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            var admission = await _context.Admissions
                .Include(a => a.Patient)
                .FirstOrDefaultAsync(a => a.Id == admissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
            if (admission == null) return NotFound();

            ViewBag.AdmissionId = admissionId;
            ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";

            ViewBag.Doctors = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                    .OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName");

            return View(new DoctorVisit
            {
                AdmissionId = admissionId,
                VisitDate = DateTime.Now,
                IsContactRecord = true
            });
        }

        [HttpPost("RecordDoctorContact/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordDoctorContact(DoctorVisit visit)
        {
            int? nurseId = GetCurrentNurseId();
            if (nurseId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Doctor");

            if (!visit.DoctorId.HasValue && string.IsNullOrWhiteSpace(visit.ExternalDoctorName))
                ModelState.AddModelError("ExternalDoctorName", "Either select a doctor or enter a name.");

            if (!ModelState.IsValid)
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == visit.AdmissionId && a.IsActive == Status.Active && a.NurseId == nurseId.Value);
                if (admission != null)
                    ViewBag.PatientName = $"{admission.Patient.FirstName} {admission.Patient.LastName}";
                ViewBag.AdmissionId = visit.AdmissionId;
                ViewBag.Doctors = new SelectList(
                    await _context.Employees
                        .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                        .OrderBy(e => e.LastName).ToListAsync(),
                    "EmployeeID", "FullName", visit.DoctorId);
                return View(visit);
            }

            var validAdmission = await _context.Admissions
                .AnyAsync(a => a.Id == visit.AdmissionId && a.IsActive == Status.Active);
            if (!validAdmission)
            {
                ModelState.AddModelError("", "Invalid admission.");
                return View(visit);
            }

            visit.IsActive = Status.Active;
            visit.IsContactRecord = true;
            _context.DoctorVisits.Add(visit);
            await _context.SaveChangesAsync();

            // --------------- NOTIFICATION TO ASSIGNED DOCTOR ---------------
            try
            {
                var admission = await _context.Admissions
                    .Include(a => a.Patient)
                    .FirstOrDefaultAsync(a => a.Id == visit.AdmissionId);

                if (admission != null && admission.DoctorId > 0)
                {
                    string nurseName = (await _context.Employees.FindAsync(nurseId.Value))?.FullName ?? "A nurse";
                    string patientName = admission.Patient.FullName;
                    string docLink = Url.Action("PatientFolder", "Doctor", new { admissionId = visit.AdmissionId });

                    await _notifService.NotifyUserAsync(
                        admission.DoctorId,
                        "Employee",
                        $"{nurseName} recorded instructions from a contact regarding patient {patientName}.",
                        docLink);
                }
            }
            catch (Exception ex) { Console.WriteLine("Notification error: " + ex.Message); }

            TempData["SuccessMessage"] = "Doctor contact recorded.";
            return RedirectToAction(nameof(DoctorVisitsByAdmission), new { admissionId = visit.AdmissionId });
        }
        // View details of a doctor visit

        [HttpGet("DoctorVisitDetails/{int:id}")]
        public async Task<IActionResult> DoctorVisitDetails(int id)
        {
            var visit = await _context.DoctorVisits
                .Include(v => v.Admission).ThenInclude(a => a.Patient)
                .Include(v => v.Doctor)
                .FirstOrDefaultAsync(v => v.Id == id);
            if (visit == null) return NotFound();
            return View(visit);
        }

        // Edit a doctor visit (e.g., update instructions after the fact)
        [HttpGet("EditDoctorVisit/{int:id}")]
        public async Task<IActionResult> EditDoctorVisit(int id)
        {
            var visit = await _context.DoctorVisits
                .Include(v => v.Admission).ThenInclude(a => a.Patient)
                .FirstOrDefaultAsync(v => v.Id == id && v.IsActive == Status.Active);
            if (visit == null) return NotFound();

            ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
            ViewBag.Doctors = new SelectList(
                await _context.Employees
                    .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                    .OrderBy(e => e.LastName).ToListAsync(),
                "EmployeeID", "FullName", visit.DoctorId);

            return View(visit);
        }

        [HttpPost("EditDoctorVisit/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDoctorVisit(int id, DoctorVisit posted)
        {
            if (id != posted.Id) return BadRequest();
            ModelState.Remove("IsActive");
            ModelState.Remove("Admission");
            ModelState.Remove("Doctor");

            if (!posted.DoctorId.HasValue && string.IsNullOrWhiteSpace(posted.ExternalDoctorName))
                ModelState.AddModelError("ExternalDoctorName", "Either select a doctor or enter a name.");

            if (!ModelState.IsValid)
            {
                var visit = await _context.DoctorVisits
                    .Include(v => v.Admission).ThenInclude(a => a.Patient)
                    .FirstOrDefaultAsync(v => v.Id == id);
                if (visit != null)
                    ViewBag.PatientName = $"{visit.Admission.Patient.FirstName} {visit.Admission.Patient.LastName}";
                ViewBag.Doctors = new SelectList(
                    await _context.Employees
                        .Where(e => e.Role == UserRole.DOCTOR && e.IsActive == Status.Active)
                        .OrderBy(e => e.LastName).ToListAsync(),
                    "EmployeeID", "FullName", posted.DoctorId);
                return View(posted);
            }

            var existing = await _context.DoctorVisits.FindAsync(id);
            if (existing == null || existing.IsActive != Status.Active) return NotFound();

            existing.DoctorId = posted.DoctorId;
            existing.ExternalDoctorName = posted.ExternalDoctorName;
            existing.VisitDate = posted.VisitDate;
            existing.Instructions = posted.Instructions;
            existing.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Doctor visit updated.";
            return RedirectToAction(nameof(DoctorVisitsByAdmission), new { admissionId = existing.AdmissionId });
        }

        [HttpPost("DeleteDoctorVisit/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDoctorVisit(int id)
        {
            var visit = await _context.DoctorVisits.FindAsync(id);
            if (visit == null) return NotFound();

            visit.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Doctor visit deactivated.";
            return RedirectToAction(nameof(DoctorVisitsByAdmission), new { admissionId = visit.AdmissionId });
        }

        [HttpPost("RestoreDoctorVisit/{int:id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreDoctorVisit(int id)
        {
            var visit = await _context.DoctorVisits.FindAsync(id);
            if (visit == null) return NotFound();

            visit.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Doctor visit reactivated.";
            return RedirectToAction(nameof(DoctorVisitsByAdmission), new { admissionId = visit.AdmissionId });
        }
    }
}