# Ward Management System

A role-based hospital ward management platform built with **ASP.NET Core MVC**. The system coordinates the full patient journey — admission, clinical care, medication, consumables, discharge planning, and patient transport — across eleven distinct staff roles plus a self-service patient portal.

---

## Table of Contents

1. [Overview](#overview)
2. [Tech Stack](#tech-stack)
3. [User Roles](#user-roles)
4. [Core Modules](#core-modules)
5. [Authentication & Account Security](#authentication--account-security)
6. [Notification System](#notification-system)
7. [Data Model Summary](#data-model-summary)
8. [Project Structure](#project-structure)
9. [Getting Started](#getting-started)
10. [Configuration](#configuration)
11. [Soft Delete Convention](#soft-delete-convention)
12. [Known Limitations](#known-limitations)
13. [License](#license)

---

## Overview

The Ward Management System digitizes hospital ward operations end-to-end:

- **Admission** of patients into beds/wards with allergy, medication, and condition capture.
- **Clinical care** — vitals, treatments, doctor visits/instructions, medication administration — recorded against a patient's active admission.
- **Prescription lifecycle** — from doctor prescribing, through a Script Manager triage step, to Pharmacist dispensing, and back to ward delivery confirmation.
- **Consumables supply chain** — from ward-level ordering, through a Supplier fulfillment/rejection workflow, to stock-take reconciliation.
- **Patient transport** — Ward Admins request porter movements; porters accept/reject/reassign and check patients in/out of the ward.
- **Discharge planning & social work** — psychosocial assessments, risk screenings, referrals, discharge plans/tasks, family meetings, and follow-up scheduling.
- **Patient self-service** — patients view their own folder, medications, instructions, appointments, discharge summaries (PDF), and manage emergency contacts.
- **In-app notifications** — a role-and-user-aware notification feed with read/unread state.

Every entity that supports lifecycle management (employees, wards, beds, medications, admissions, prescriptions, etc.) uses a **soft-delete** (`Status.Active` / `Status.Inactive`) pattern rather than hard deletes, so historical records remain intact and reversible.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core MVC (.NET) |
| ORM | Entity Framework Core |
| Auth | Cookie authentication (`CookieAuthenticationDefaults`), claims-based roles |
| Password hashing | BCrypt.Net |
| Two-Factor Auth | TOTP via `ITwoFactorService` (QR code + recovery codes) |
| Bot protection | Google reCAPTCHA (`ReCaptchaService`) |
| Email | `IEmailService` (welcome emails, password reset, email-change confirmation) |
| PDF generation | `HtmlToPdfConverter` (Rotativa-style HTML→PDF rendering) |
| Barcode generation | ZXing.SkiaSharp (`CODE_128` wristband barcodes) |
| Notifications | `INotificationService` (per-user and per-role notifications) |

---

## User Roles

The system defines the following roles via the `UserRole` enum, each with a dedicated controller, dashboard, and `[Authorize(Roles = "...")]` guard:

| Role | Controller | Primary Responsibility |
|---|---|---|
| **ADMINISTRATOR** | `AdminController` | System-wide configuration: employees, wards, beds, medications, allergies, conditions, hospital info, locations |
| **WARDADMIN** | `WardAdminController` | Patient admission, bed assignment, doctor/nurse/social worker assignment, discharge, porter movement requests, follow-up scheduling, patient records |
| **DOCTOR** | `DoctorController` | Patient folder review, doctor visits, treatments, prescriptions, discharge initiation, scheduled visits & instructions |
| **NURSE** | `NurseController` | Vitals, treatments, medication administration (Schedule 1–4 only), doctor-contact recording |
| **NURSINGSISTER** | `NursingSisterController` | Medication administration with **no schedule restriction** (can administer Schedule 5+) |
| **SCRIPTMANAGER** | `ScriptManagerController` | Prescription intake/creation, allergy-checked forwarding to pharmacy, STAT flagging, receiving dispensed medication back onto the ward |
| **PHARMACIST** | `PharmacistController` | Dispensing forwarded prescriptions with batch tracking |
| **CONSUMABLESMANAGER** | `ConsumablesManagerController` | Consumable stock CRUD, ordering from suppliers, receiving deliveries, weekly stock takes |
| **SUPPLIER** | `SupplierController` | Order fulfillment (full/partial), rejection, shipping/tracking metadata, order history |
| **PORTER** | `PorterController` | Accepting/rejecting/reassigning movement requests, check-out/check-in of patients, zone tracking |
| **SOCIALWORKER** | `SocialWorkerController` | Psychosocial assessments, risk screenings, discharge plans & tasks, referrals, follow-ups, family contact logs, family meetings, PDF social work reports |
| **PATIENT** | `PatientController` | Self-service dashboard, profile, admissions, medications, instructions, appointments, discharge summary (PDF), follow-up requests, emergency contacts |

All controllers derive the acting user's identity from `ClaimTypes.NameIdentifier` and validate the `ClaimTypes.Role` claim before granting access, in addition to the `[Authorize(Roles = "...")]` attribute.

---

## Core Modules

### 1. Patient Admission (Ward Admin)
- Two-step admission flow: **Step 1** captures/creates the patient record (with existing-patient lookup by SA ID number); **Step 2** assigns doctor, nurse, **social worker**, bed, and links allergies/medications/conditions.
- Cascading AJAX endpoint (`GetMedicationAssociations`) resolves allergy/condition warnings tied to selected medications.
- Bed occupancy is toggled automatically on admission/discharge/edit.
- Notifies the assigned doctor, nurse, and social worker on admission or reassignment; notifies previously-assigned staff when they are replaced.

### 2. Clinical Documentation (Doctor / Nurse / Nursing Sister)
- **Patient Folder** aggregates vitals, treatments, medication administrations, doctor visits, and movement history for an admission.
- **Doctor Visits** support both live visits and **contact records** (instructions relayed by phone, recorded by a nurse on the doctor's behalf, with either an internal doctor or a free-text external doctor name).
- **Scheduled Visits & Instructions** let doctors book a future visit, then attach instructions later — the assigned nurse is notified when instructions are written.
- **Medication administration** is schedule-gated: `NurseController` blocks Schedule ≥5 medications; `NursingSisterController` has no such restriction.
- **Treatments** are recorded per admission by whichever clinical role is treating the patient.

### 3. Prescription Pipeline (Doctor → Script Manager → Pharmacist → Ward)
```
Doctor prescribes ──▶ ScriptStatus.New
        │
        ▼  (allergy check performed here)
Script Manager forwards ──▶ ScriptStatus.ForwardedToPharmacy
        │                         (STAT scripts notify all Pharmacists immediately)
        ▼
Pharmacist dispenses ──▶ ScriptStatus.Dispensed  (batch number, notifies Script Manager)
        │
        ▼
Script Manager receives & verifies ──▶ ScriptStatus.Delivered
        │
        └─(alternative)─▶ Script Manager rejects dispensed item ──▶ ScriptStatus.Rejected
```
- Duplicate active-prescription detection (`HasActiveDuplicate`) warns when a patient already has an active script for the same medication.
- Bulk-forward and STAT-toggle actions are available to Script Managers.
- Prescription labels can be printed per script.

### 4. Consumables & Supply Chain
```
Consumables Manager requests order ──▶ OrderStatus.Ordered
        │
        ▼
Supplier fulfills (full or partial) ──▶ OrderStatus.Fulfilled / PartiallyFulfilled
        │            (or) Supplier rejects ──▶ OrderStatus.Rejected
        ▼
Consumables Manager receives ──▶ OrderStatus.Complete (stock quantity incremented)
```
- Weekly **Stock Takes**: a header record is created, then all active consumables are counted against system quantities, capturing variances in `StockTakeItem`.
- Received orders can be soft-deleted, which **reverses the stock addition**, and restored, which **re-applies it** — keeping stock levels consistent with order history.
- Suppliers can record shipping references, courier name, tracking links, and batch numbers on fulfillment.

### 5. Patient Movement / Porter Workflow
- Ward Admin submits a `CheckOutRequest` to a specific porter.
- Porter can **Accept** (with optional ETA), **Reject** (with reason), or the request can be **reassigned** to another porter by either the porter or the Ward Admin.
- On confirmed check-out, the admission's `CurrentLocation` is updated and a movement record is written; check-in reverses this and clears the location.
- Porters can set their **current zone** for location tracking (`PorterLocations` view for Ward Admins).
- Full movement history is viewable per admission.

### 6. Social Work & Discharge Planning
- **Psychosocial Assessment** (one active per admission): social history, support network, financial concerns, substance use, mental health status.
- **Risk Screenings**: scored, auto-classified into Low/Medium/High risk (`CalculateRiskLevel`), by screening type.
- **Discharge Plans**: staged workflow — `Pending → InProgress → ReadyForReview → Approved → Implemented`, with the ability to revert a stage. Each plan can have **tasks** with due dates and completion tracking.
- **Referrals** to external organizations with outcome tracking.
- **Needs Checklist**: auto-seeded with common needs (Transport, Home Care, Equipment, Meals, Counselling) per admission, toggleable and extensible.
- **Family Contact Logs** and **Family Meetings** (with auto-attached doctor/nurse plus optional extra attendees, and notifications sent to all attendees).
- **Follow-Ups**: scheduled post-discharge (phone call, etc.), only permitted on discharged (inactive) admissions.
- A consolidated **Social Work Report (PDF)** aggregates all of the above for an admission.

### 7. Patient Self-Service Portal
- Dashboard, profile view/edit, admissions history, and full patient-folder read access scoped to the logged-in patient.
- **My Medications** view cross-references active prescriptions with the most recent administration timestamp.
- **Discharge Summary (PDF)** and **Social Work Report** generation for discharged admissions.
- **Follow-up requests** for outpatient appointments after discharge, routed to the Ward Admin and preferred doctor.
- **Emergency contacts** CRUD.
- **My Appointments** lists upcoming (non-contact) doctor visits.

---

## Authentication & Account Security

Handled centrally by `AccountController`, shared across both `Employee` and `Patient` identities:

- **Password hashing**: BCrypt, with automatic **upgrade-in-place** for any legacy plaintext-equivalent hashes (`VerifyAndUpgradePassword`).
- **Password policy**: minimum 8 characters, requires uppercase, digit, and special character (`IsPasswordComplex`).
- **Password history**: last 5 password hashes retained per user; reuse is blocked (`IsPasswordPreviouslyUsed` / `AddPasswordToHistory`).
- **Account lockout**: 5 failed attempts → 15-minute lockout, tracked independently for employees and patients.
- **reCAPTCHA**: required on every login attempt.
- **Two-Factor Authentication (TOTP)**:
  - Setup generates a secret key + QR code (`SetupTwoFactor`) and one-time **recovery codes** (hashed with BCrypt before storage).
  - Login enforces a **2FA challenge** step unless the current device is marked trusted.
  - Recovery codes can be used in place of a TOTP code and are regenerable.
  - 2FA can be disabled with password re-confirmation.
- **Trusted Devices**: a persistent `.DeviceId` cookie + `UserDevice` record; devices can be marked trusted at 2FA-challenge time to skip future 2FA prompts, and can be individually revoked (`ManageDevices` / `RevokeDevice`).
- **Password reset**: emailed time-limited token (1 hour expiry), enforces the password policy and history rules.
- **Change email**: requires confirmation via emailed token (24-hour expiry) before the new address takes effect; duplicate-email checks span both `Employees` and `Patients`.
- **Forced password change**: new employee/patient accounts are provisioned with a random temporary password and `MustChangePassword = true`, redirecting to the change-password flow on first login.
- **Account deactivation**: self-service (patient) and admin-driven (employee), both soft-delete via `Status.Inactive`.

---

## Notification System

`NotificationController` + `INotificationService` provide a unified feed:

- Notifications can target a **specific user** (`UserId` + `UserType`), a **whole role** (`Role`), or be **global** (`UserId == null && Role == null`).
- Dropdown endpoint (`Latest`) returns the 10 most recent active notifications as JSON for polling.
- Supports mark-as-read (single / all) and soft-delete (single / clear-all), scoped to the requesting user's visibility rules.
- Triggered throughout the app on: employee/patient account creation, patient admission/reassignment, doctor visit scheduling & instructions, prescription creation/forwarding/dispensing/rejection, STAT prescriptions (broadcast to all Pharmacists), porter movement request/accept/reject/reassign, discharge initiation, follow-up requests, and family meeting invitations.

---

## Data Model Summary

Key entities referenced across controllers (see `WARDMANAGEMENTSYSTEM.Models` for full definitions):

- **Identity**: `Employee`, `Patient`, `UserDevice`
- **Ward infrastructure**: `Ward`, `Bed`, `HospitalInfo`, `HospitalLocation`
- **Clinical reference data**: `Medication`, `Allergy`, `Condition`, `AllergyMedication`, `ConditionMedication`
- **Admission & clinical record**: `Admission`, `AdmissionAllergy`, `AdmissionMedication`, `AdmissionCondition`, `Vitals`, `Treatment`, `DoctorVisit`, `MedicationAdministration`, `PatientMovement`
- **Pharmacy**: `Prescription` (with `ScriptStatus`), `ScriptManager`/`Pharmacist` links
- **Consumables**: `Consumable`, `ConsumableOrder`, `ConsumableOrderBatch`, `StockTake`, `StockTakeItem`
- **Social work**: `DischargePlan`, `DischargePlanTask`, `PsychosocialAssessment`, `RiskScreening`, `Referral`, `PatientNeed`, `FollowUp`, `FollowUpRequest`, `FamilyContactLog`, `FamilyMeeting`, `FamilyMeetingAttendee`
- **Patient extras**: `EmergencyContact`
- **Notifications**: `Notification`

Most entities carry an `IsActive` (`Status.Active` / `Status.Inactive`) field for soft-delete semantics.

---

## Project Structure

```
WARDMANAGEMENTSYSTEM/
├── AppStatus/              # Status, UserRole, OrderStatus, ScriptStatus, etc. enums
├── Components/             # Shared rendering helpers (e.g. RenderViewAsync for PDF views)
├── Controllers/
│   ├── AccountController.cs
│   ├── AdminController.cs
│   ├── ConsumablesManagerController.cs
│   ├── DoctorController.cs
│   ├── NotificationController.cs
│   ├── NurseController.cs
│   ├── NursingSisterController.cs
│   ├── PatientController.cs
│   ├── PharmacistController.cs
│   ├── PorterController.cs
│   ├── ScriptManagerController.cs
│   ├── SocialWorkerController.cs
│   ├── SupplierController.cs
│   └── WardAdminController.cs
├── Data/
│   └── WardDbContext.cs
├── Models/                 # EF Core entities
├── Services/                # IEmailService, ITwoFactorService, INotificationService, ReCaptchaService
├── ViewModel/                # DTOs / view-specific models
└── Views/                   # Razor views per controller
```

---

## Getting Started

### Prerequisites
- .NET SDK (6.0 or later, matching the project's target framework)
- SQL Server (or the configured EF Core provider)
- A Google reCAPTCHA site/secret key pair
- SMTP credentials (or equivalent) for `IEmailService`

### Setup

1. **Clone and restore**
   ```bash
   git clone <repository-url>
   cd WARDMANAGEMENTSYSTEM
   dotnet restore
   ```

2. **Configure `appsettings.json`** (see [Configuration](#configuration) below) with your connection string, reCAPTCHA keys, and email settings.

3. **Apply migrations**
   ```bash
   dotnet ef database update
   ```

4. **Run the application**
   ```bash
   dotnet run
   ```

5. **Seed an initial Administrator** account directly in the database (or via a seed script) since account creation for employees is otherwise gated behind an authenticated Administrator/Ward Admin session.

---

## Configuration

Typical `appsettings.json` sections required by this project:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=WardManagementSystem;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "ReCaptcha": {
    "SiteKey": "your-site-key",
    "SecretKey": "your-secret-key"
  },
  "EmailSettings": {
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "SenderEmail": "noreply@example.com",
    "SenderPassword": "your-app-password"
  }
}
```

Cookie authentication is configured with `CookieAuthenticationDefaults.AuthenticationScheme` in `Program.cs`/`Startup.cs` — ensure `IsPersistent` / `ExpiresUtc` settings there align with the "Remember Me" behavior used in `AccountController`.

---

## Soft Delete Convention

The system **never hard-deletes** operational data. Instead:

- Entities expose `IsActive` set to `Status.Active` or `Status.Inactive`.
- "Delete" actions in controllers set `IsActive = Status.Inactive`.
- "Restore" actions set it back to `Status.Active`.
- List views typically default to `status = "Active"` and offer an `"All"` filter to include inactive records.
- Some flows apply compensating logic on delete/restore (e.g. reversing/reapplying stock quantities when a received consumables order is deactivated/restored, or releasing/re-occupying a bed when an admission is deactivated/restored).

---

## Known Limitations

- `DeleteMovement` in `WardAdminController` does not currently perform an actual removal or state change — it saves without modification (see inline comment in the source).
- Email delivery failures are caught and surfaced as a modified success message rather than blocking the underlying operation (e.g. employee/patient creation still succeeds even if the welcome email fails).
- Two-factor recovery codes and password history are stored as JSON-serialized lists on the user record rather than in normalized tables.

---

## License

Internal / academic project — license terms to be defined by the project owner.