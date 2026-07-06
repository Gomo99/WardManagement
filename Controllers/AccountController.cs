using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;
using WARDMANAGEMENTSYSTEM.ViewModel;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    // [Route("[controller]")]  ← REMOVED
    public class AccountController : Controller
    {
        private readonly WardDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ITwoFactorService _twoFactorService;
        private const string DeviceIdCookieName = ".DeviceId";

        private const string SuccessMessageKey = "SuccessMessage";
        private const string ErrorMessageKey = "ErrorMessage";

        private const string LoginAttemptsCookieName = ".LoginAttempts";
        private readonly ReCaptchaService _recaptchaService;

        public AccountController(WardDbContext context, IEmailService emailService,
            ITwoFactorService twoFactorService, ReCaptchaService recaptchaService)
        {
            _context = context;
            _emailService = emailService;
            _twoFactorService = twoFactorService;
            _recaptchaService = recaptchaService;
        }

        // ======================================================================
        //  HELPERS
        // ======================================================================
        private void SetSuccess(string message) => TempData[SuccessMessageKey] = message;
        private void SetError(string message) => TempData[ErrorMessageKey] = message;

        private int? GetCurrentUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            return id;
        }

        private string? GetCurrentUserRole() => User.FindFirstValue(ClaimTypes.Role);

        private ClaimsPrincipal BuildEmployeeClaims(Employee emp)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, emp.EmployeeID.ToString()),
                new Claim(ClaimTypes.Name, emp.Email),
                new Claim(ClaimTypes.Email, emp.Email),
                new Claim(ClaimTypes.GivenName, emp.FirstName),
                new Claim(ClaimTypes.Surname, emp.LastName),
                new Claim(ClaimTypes.Role, emp.Role.ToString())
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        private ClaimsPrincipal BuildPatientClaims(Patient pat)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, pat.Id.ToString()),
                new Claim(ClaimTypes.Name, pat.Email),
                new Claim(ClaimTypes.Email, pat.Email),
                new Claim(ClaimTypes.GivenName, pat.FirstName),
                new Claim(ClaimTypes.Surname, pat.LastName),
                new Claim(ClaimTypes.Role, UserRole.PATIENT.ToString())
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        private async Task SignInAsync(ClaimsPrincipal principal, bool isPersistent)
        {
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = isPersistent,
                    ExpiresUtc = isPersistent ? DateTimeOffset.UtcNow.AddDays(7) : null
                });
        }

        private IActionResult RedirectToDashboard(UserRole? role = null)
        {
            if (role == null && User.Identity?.IsAuthenticated == true)
            {
                var roleStr = GetCurrentUserRole();
                Enum.TryParse<UserRole>(roleStr, out var parsed);
                role = parsed;
            }

            return role switch
            {
                UserRole.ADMINISTRATOR => RedirectToAction("Dashboard", "Admin"),
                UserRole.DOCTOR => RedirectToAction("Dashboard", "Doctor"),
                UserRole.PATIENT => RedirectToAction("Dashboard", "Patient"),
                UserRole.NURSE => RedirectToAction("Dashboard", "Nurse"),
                UserRole.NURSINGSISTER => RedirectToAction("Dashboard", "NursingSister"),
                UserRole.WARDADMIN => RedirectToAction("Dashboard", "WardAdmin"),
                UserRole.SCRIPTMANAGER => RedirectToAction("Dashboard", "ScriptManager"),
                UserRole.CONSUMABLESMANAGER => RedirectToAction("Dashboard", "ConsumablesManager"),
                UserRole.PHARMACIST => RedirectToAction("Dashboard", "Pharmacist"),
                UserRole.PORTER => RedirectToAction("Dashboard", "Porter"),
                UserRole.SOCIALWORKER => RedirectToAction("Dashboard", "SocialWorker"),
                UserRole.SUPPLIER => RedirectToAction("Dashboard", "Supplier"),
                _ => RedirectToAction("Login", "Account")
            };
        }

        private IActionResult RedirectToSavedUrl(string? returnUrl, UserRole? role = null)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToDashboard(role);
        }

        private static string HashPassword(string pw) => BCrypt.Net.BCrypt.HashPassword(pw);
        private static bool VerifyPassword(string pw, string hash) =>
            !string.IsNullOrEmpty(hash) && (hash.StartsWith("$2") ? BCrypt.Net.BCrypt.Verify(pw, hash) : pw == hash);

        private static bool VerifyAndUpgradePassword(string pw, ref string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash)) return false;
            if (storedHash.StartsWith("$2")) return BCrypt.Net.BCrypt.Verify(pw, storedHash);
            if (pw == storedHash)
            {
                storedHash = HashPassword(pw);
                return true;
            }
            return false;
        }

        private static bool IsPasswordComplex(string pw) =>
            !string.IsNullOrEmpty(pw) && pw.Length >= 8 &&
            Regex.IsMatch(pw, @"[A-Z]") && Regex.IsMatch(pw, @"\d") &&
            Regex.IsMatch(pw, @"[^a-zA-Z0-9\s]");

        private static int CountRecoveryCodes(string? json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            try { return JsonSerializer.Deserialize<List<string>>(json)?.Count ?? 0; }
            catch { return 0; }
        }

        // ---- Device helpers ----
        private string GetOrCreateDeviceId()
        {
            if (Request.Cookies.TryGetValue(DeviceIdCookieName, out var existing))
                return existing;
            var newId = Guid.NewGuid().ToString("N");
            Response.Cookies.Append(DeviceIdCookieName, newId, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            });
            return newId;
        }

        private async Task<UserDevice> GetOrCreateDeviceRecord(int userId, string userType, string deviceId, string? ip)
        {
            var device = await _context.UserDevices
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.UserId == userId && d.UserType == userType);
            if (device == null)
            {
                device = new UserDevice
                {
                    UserId = userId,
                    UserType = userType,
                    DeviceId = deviceId,
                    DeviceName = Request.Headers["User-Agent"].ToString(),
                    IpAddress = ip,
                    IsTrusted = false,
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now
                };
                _context.UserDevices.Add(device);
            }
            else
            {
                device.LastSeen = DateTime.Now;
                device.IpAddress = ip;
            }
            await _context.SaveChangesAsync();
            return device;
        }

        private const int PasswordHistoryLimit = 5;

        private bool IsPasswordPreviouslyUsed(string newPassword, string? previousHashesJson)
        {
            if (string.IsNullOrEmpty(previousHashesJson))
                return false;

            var previousHashes = JsonSerializer.Deserialize<List<string>>(previousHashesJson);
            if (previousHashes == null)
                return false;

            foreach (var hash in previousHashes)
            {
                if (BCrypt.Net.BCrypt.Verify(newPassword, hash))
                    return true;
            }
            return false;
        }

        private string AddPasswordToHistory(string currentPasswordHash, string? previousHashesJson)
        {
            var hashes = string.IsNullOrEmpty(previousHashesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(previousHashesJson) ?? new List<string>();

            hashes.Insert(0, currentPasswordHash);

            if (hashes.Count > PasswordHistoryLimit)
                hashes = hashes.Take(PasswordHistoryLimit).ToList();

            return JsonSerializer.Serialize(hashes);
        }

        // ======================================================================
        //  LOGIN (Employee & Patient)
        // ======================================================================
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToDashboard();

            ViewData["ReturnUrl"] = returnUrl;
            ViewBag.ShowCaptcha = true;
            ViewBag.SiteKey = _recaptchaService.SiteKey;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(model);

            var token = Request.Form["g-recaptcha-response"];
            if (string.IsNullOrEmpty(token))
            {
                SetError("Please complete the CAPTCHA.");
                ViewBag.ShowCaptcha = true;
                ViewBag.SiteKey = _recaptchaService.SiteKey;
                return View(model);
            }

            var isValid = await _recaptchaService.ValidateTokenAsync(token);
            if (!isValid)
            {
                SetError("CAPTCHA verification failed. Please try again.");
                ViewBag.ShowCaptcha = true;
                ViewBag.SiteKey = _recaptchaService.SiteKey;
                return View(model);
            }

            // --- Employee ---
            var emp = await _context.Employees.FirstOrDefaultAsync(e => e.Email == model.UserNameorEmail);
            if (emp != null)
            {
                if (emp.IsActive != Status.Active) { SetError("Account is inactive."); return View(model); }
                if (emp.LockoutEnd.HasValue && emp.LockoutEnd > DateTime.Now)
                { SetError($"Account locked. Try again after {emp.LockoutEnd:HH:mm}."); return View(model); }

                string hash = emp.PasswordHash;
                bool ok = VerifyAndUpgradePassword(model.Password, ref hash);
                if (hash != emp.PasswordHash) emp.PasswordHash = hash;
                if (!ok)
                {
                    emp.FailedLoginAttempts++;
                    if (emp.FailedLoginAttempts >= 5)
                    { emp.LockoutEnd = DateTime.Now.AddMinutes(15); emp.FailedLoginAttempts = 0; SetError("Too many failed attempts. Locked 15 min."); }
                    else SetError($"Invalid credentials. {5 - emp.FailedLoginAttempts} attempts left.");
                    await _context.SaveChangesAsync();
                    return View(model);
                }
                emp.FailedLoginAttempts = 0; emp.LockoutEnd = null;
                await _context.SaveChangesAsync();

                string deviceId = GetOrCreateDeviceId();
                var device = await GetOrCreateDeviceRecord(emp.EmployeeID, "Employee", deviceId,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                if (emp.IsTwoFactorEnabled && !string.IsNullOrEmpty(emp.TwoFactorSecretKey))
                {
                    if (device.IsTrusted)
                    {
                        await SignInAsync(BuildEmployeeClaims(emp), model.RememberMe);
                        if (emp.MustChangePassword) return RedirectToAction("ChangePassword");
                        return RedirectToSavedUrl(returnUrl, emp.Role);
                    }
                    TempData["2fa_pending_id"] = emp.EmployeeID.ToString();
                    TempData["2fa_pending_type"] = "Employee";
                    TempData["2fa_remember_me"] = model.RememberMe.ToString();
                    TempData["2fa_return_url"] = returnUrl ?? string.Empty;
                    return RedirectToAction("TwoFactorChallenge");
                }

                await SignInAsync(BuildEmployeeClaims(emp), model.RememberMe);
                if (emp.MustChangePassword) return RedirectToAction("ChangePassword");
                return RedirectToSavedUrl(returnUrl, emp.Role);
            }

            // --- Patient ---
            var pat = await _context.Patients.FirstOrDefaultAsync(p => p.Email == model.UserNameorEmail);
            if (pat != null)
            {
                if (pat.IsActive != Status.Active) { SetError("Account is inactive."); return View(model); }
                if (pat.LockoutEnd.HasValue && pat.LockoutEnd > DateTime.Now)
                { SetError($"Account locked. Try again after {pat.LockoutEnd:HH:mm}."); return View(model); }

                string hash = pat.PasswordHash;
                bool ok = VerifyAndUpgradePassword(model.Password, ref hash);
                if (hash != pat.PasswordHash) pat.PasswordHash = hash;
                if (!ok)
                {
                    pat.FailedLoginAttempts++;
                    if (pat.FailedLoginAttempts >= 5)
                    { pat.LockoutEnd = DateTime.Now.AddMinutes(15); pat.FailedLoginAttempts = 0; SetError("Too many failed attempts. Locked 15 min."); }
                    else SetError($"Invalid credentials. {5 - pat.FailedLoginAttempts} attempts left.");
                    await _context.SaveChangesAsync();
                    return View(model);
                }
                pat.FailedLoginAttempts = 0; pat.LockoutEnd = null;
                await _context.SaveChangesAsync();

                string deviceId = GetOrCreateDeviceId();
                var device = await GetOrCreateDeviceRecord(pat.Id, "Patient", deviceId,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                if (pat.IsTwoFactorEnabled && !string.IsNullOrEmpty(pat.TwoFactorSecretKey))
                {
                    if (device.IsTrusted)
                    {
                        await SignInAsync(BuildPatientClaims(pat), model.RememberMe);
                        if (pat.MustChangePassword) return RedirectToAction("ChangePassword");
                        return RedirectToSavedUrl(returnUrl, UserRole.PATIENT);
                    }
                    TempData["2fa_pending_id"] = pat.Id.ToString();
                    TempData["2fa_pending_type"] = "Patient";
                    TempData["2fa_remember_me"] = model.RememberMe.ToString();
                    TempData["2fa_return_url"] = returnUrl ?? string.Empty;
                    return RedirectToAction("TwoFactorChallenge");
                }

                await SignInAsync(BuildPatientClaims(pat), model.RememberMe);
                if (pat.MustChangePassword) return RedirectToAction("ChangePassword");
                return RedirectToSavedUrl(returnUrl, UserRole.PATIENT);
            }

            SetError("Invalid email or password.");
            return View(model);
        }

        // ======================================================================
        //  TWO‑FACTOR CHALLENGE
        // ======================================================================
        [Authorize]
        [HttpGet]
        public IActionResult TwoFactorChallenge()
        {
            if (TempData["2fa_pending_id"] == null) return RedirectToAction("Login");
            var model = new TwoFactorChallengeViewModel
            {
                ReturnUrl = TempData["2fa_return_url"]?.ToString() ?? string.Empty
            };
            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TwoFactorChallenge(TwoFactorChallengeViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var pendingId = TempData["2fa_pending_id"]?.ToString();
            var pendingType = TempData["2fa_pending_type"]?.ToString();
            var rememberMe = TempData["2fa_remember_me"]?.ToString() == "True";
            var returnUrl = TempData["2fa_return_url"]?.ToString();

            if (string.IsNullOrEmpty(pendingId) || string.IsNullOrEmpty(pendingType))
                return RedirectToAction("Login");
            if (!int.TryParse(pendingId, out int userId))
                return RedirectToAction("Login");

            string? secretKey = null;
            string? recoveryJson = null;
            UserRole role = UserRole.PATIENT;
            if (pendingType == "Employee")
            {
                var e = await _context.Employees.FindAsync(userId);
                if (e == null) return RedirectToAction("Login");
                secretKey = e.TwoFactorSecretKey;
                recoveryJson = e.TwoFactorRecoveryCodes;
                role = e.Role;
            }
            else if (pendingType == "Patient")
            {
                var p = await _context.Patients.FindAsync(userId);
                if (p == null) return RedirectToAction("Login");
                secretKey = p.TwoFactorSecretKey;
                recoveryJson = p.TwoFactorRecoveryCodes;
                role = UserRole.PATIENT;
            }
            else return RedirectToAction("Login");

            bool isValid = false;
            if (model.UseRecoveryCode && !string.IsNullOrEmpty(model.RecoveryCode))
            {
                isValid = _twoFactorService.VerifyRecoveryCode(recoveryJson ?? string.Empty,
                    model.RecoveryCode, out string updated);
                if (isValid)
                {
                    if (pendingType == "Employee")
                        (_context.Employees.Find(userId))!.TwoFactorRecoveryCodes = updated;
                    else
                        (_context.Patients.Find(userId))!.TwoFactorRecoveryCodes = updated;
                }
            }
            else if (!string.IsNullOrEmpty(model.Code))
            {
                isValid = _twoFactorService.VerifyCode(secretKey!, model.Code);
            }

            if (!isValid)
            {
                SetError("Invalid authentication code. Please try again.");
                TempData["2fa_pending_id"] = userId.ToString();
                TempData["2fa_pending_type"] = pendingType;
                TempData["2fa_remember_me"] = rememberMe.ToString();
                TempData["2fa_return_url"] = returnUrl ?? string.Empty;
                return View(model);
            }

            if (model.TrustDevice)
            {
                string deviceId = GetOrCreateDeviceId();
                var device = await _context.UserDevices
                    .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.UserId == userId && d.UserType == pendingType);
                if (device != null) device.IsTrusted = true;
            }

            await _context.SaveChangesAsync();

            ClaimsPrincipal principal;
            if (pendingType == "Employee")
            {
                var emp = await _context.Employees.FindAsync(userId);
                principal = BuildEmployeeClaims(emp!);
            }
            else
            {
                var pat = await _context.Patients.FindAsync(userId);
                principal = BuildPatientClaims(pat!);
            }
            await SignInAsync(principal, rememberMe);

            TempData.Remove("2fa_pending_id"); TempData.Remove("2fa_pending_type");
            TempData.Remove("2fa_remember_me"); TempData.Remove("2fa_return_url");

            if (pendingType == "Employee")
            {
                var e = await _context.Employees.FindAsync(userId);
                if (e!.MustChangePassword) return RedirectToAction("ChangePassword");
            }
            else
            {
                var p = await _context.Patients.FindAsync(userId);
                if (p!.MustChangePassword) return RedirectToAction("ChangePassword");
            }

            return RedirectToSavedUrl(returnUrl, role);
        }

        // ======================================================================
        //  TWO‑FACTOR SETUP & MANAGEMENT
        // ======================================================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> SetupTwoFactor()
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();

            if (role == UserRole.PATIENT.ToString())
            {
                var p = await _context.Patients.FindAsync(userId.Value);
                if (p == null || p.IsTwoFactorEnabled) return RedirectToAction("ManageTwoFactor");
                var secretKey = _twoFactorService.GenerateSecretKey();
                p.TwoFactorSecretKey = secretKey;
                await _context.SaveChangesAsync();

                var uri = _twoFactorService.GetQrCodeUri(secretKey, p.Email, "WardSystem");
                var qr = _twoFactorService.GenerateQrCodePng(uri);
                return View(new TwoFactorSetupViewModel
                {
                    SecretKey = secretKey,
                    QrCodeBase64 = Convert.ToBase64String(qr)
                });
            }
            else
            {
                var e = await _context.Employees.FindAsync(userId.Value);
                if (e == null || e.IsTwoFactorEnabled) return RedirectToAction("ManageTwoFactor");
                var secretKey = _twoFactorService.GenerateSecretKey();
                e.TwoFactorSecretKey = secretKey;
                await _context.SaveChangesAsync();

                var uri = _twoFactorService.GetQrCodeUri(secretKey, e.Email, "WardSystem");
                var qr = _twoFactorService.GenerateQrCodePng(uri);
                return View(new TwoFactorSetupViewModel
                {
                    SecretKey = secretKey,
                    QrCodeBase64 = Convert.ToBase64String(qr)
                });
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetupTwoFactor(TwoFactorSetupViewModel model)
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();

            string? secretKey;
            if (role == UserRole.PATIENT.ToString())
            {
                var p = await _context.Patients.FindAsync(userId.Value);
                if (p == null) return RedirectToAction("Login");
                secretKey = p.TwoFactorSecretKey;
                if (!ModelState.IsValid)
                {
                    var uri = _twoFactorService.GetQrCodeUri(secretKey!, p.Email, "WardSystem");
                    model.QrCodeBase64 = Convert.ToBase64String(_twoFactorService.GenerateQrCodePng(uri));
                    model.SecretKey = secretKey!;
                    return View(model);
                }
                if (!_twoFactorService.VerifyCode(secretKey!, model.VerificationCode))
                {
                    SetError("Invalid verification code.");
                    var uri = _twoFactorService.GetQrCodeUri(secretKey!, p.Email, "WardSystem");
                    model.QrCodeBase64 = Convert.ToBase64String(_twoFactorService.GenerateQrCodePng(uri));
                    model.SecretKey = secretKey!;
                    return View(model);
                }
                p.IsTwoFactorEnabled = true;
                var codes = _twoFactorService.GenerateRecoveryCodes();
                p.TwoFactorRecoveryCodes = JsonSerializer.Serialize(codes.Select(c => HashPassword(c)).ToList());
                await _context.SaveChangesAsync();
                TempData["ShowRecoveryCodes"] = "true";
                return RedirectToAction("ShowRecoveryCodes", new { codes = string.Join(",", codes) });
            }
            else
            {
                var e = await _context.Employees.FindAsync(userId.Value);
                if (e == null) return RedirectToAction("Login");
                secretKey = e.TwoFactorSecretKey;
                if (!ModelState.IsValid)
                {
                    var uri = _twoFactorService.GetQrCodeUri(secretKey!, e.Email, "WardSystem");
                    model.QrCodeBase64 = Convert.ToBase64String(_twoFactorService.GenerateQrCodePng(uri));
                    model.SecretKey = secretKey!;
                    return View(model);
                }
                if (!_twoFactorService.VerifyCode(secretKey!, model.VerificationCode))
                {
                    SetError("Invalid verification code.");
                    var uri = _twoFactorService.GetQrCodeUri(secretKey!, e.Email, "WardSystem");
                    model.QrCodeBase64 = Convert.ToBase64String(_twoFactorService.GenerateQrCodePng(uri));
                    model.SecretKey = secretKey!;
                    return View(model);
                }
                e.IsTwoFactorEnabled = true;
                var codes = _twoFactorService.GenerateRecoveryCodes();
                e.TwoFactorRecoveryCodes = JsonSerializer.Serialize(codes.Select(c => HashPassword(c)).ToList());
                await _context.SaveChangesAsync();
                TempData["ShowRecoveryCodes"] = "true";
                return RedirectToAction("ShowRecoveryCodes", new { codes = string.Join(",", codes) });
            }
        }

        [Authorize]
        [HttpGet]
        public IActionResult ShowRecoveryCodes(string codes)
        {
            if (string.IsNullOrEmpty(codes)) return RedirectToAction("ManageTwoFactor");
            return View(new TwoFactorRecoveryCodesViewModel { PlainCodes = codes.Split(',').ToList() });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ManageTwoFactor()
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();

            bool enabled; int left;
            if (role == UserRole.PATIENT.ToString())
            {
                var p = await _context.Patients.FindAsync(userId.Value);
                if (p == null) return RedirectToAction("Login");
                enabled = p.IsTwoFactorEnabled;
                left = CountRecoveryCodes(p.TwoFactorRecoveryCodes);
            }
            else
            {
                var e = await _context.Employees.FindAsync(userId.Value);
                if (e == null) return RedirectToAction("Login");
                enabled = e.IsTwoFactorEnabled;
                left = CountRecoveryCodes(e.TwoFactorRecoveryCodes);
            }
            return View(new ManageTwoFactorViewModel { IsTwoFactorEnabled = enabled, RecoveryCodesLeft = left });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegenerateRecoveryCodes()
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();

            if (role == UserRole.PATIENT.ToString())
            {
                var p = await _context.Patients.FindAsync(userId.Value);
                if (p == null || !p.IsTwoFactorEnabled) return RedirectToAction("SetupTwoFactor");
                var codes = _twoFactorService.GenerateRecoveryCodes();
                p.TwoFactorRecoveryCodes = JsonSerializer.Serialize(codes.Select(c => HashPassword(c)).ToList());
                await _context.SaveChangesAsync();
                TempData["ShowRecoveryCodes"] = "true";
                return RedirectToAction("ShowRecoveryCodes", new { codes = string.Join(",", codes) });
            }
            else
            {
                var e = await _context.Employees.FindAsync(userId.Value);
                if (e == null || !e.IsTwoFactorEnabled) return RedirectToAction("SetupTwoFactor");
                var codes = _twoFactorService.GenerateRecoveryCodes();
                e.TwoFactorRecoveryCodes = JsonSerializer.Serialize(codes.Select(c => HashPassword(c)).ToList());
                await _context.SaveChangesAsync();
                TempData["ShowRecoveryCodes"] = "true";
                return RedirectToAction("ShowRecoveryCodes", new { codes = string.Join(",", codes) });
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableTwoFactor(string password)
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();

            if (role == UserRole.PATIENT.ToString())
            {
                var p = await _context.Patients.FindAsync(userId.Value);
                if (p == null) return RedirectToAction("Login");
                if (!VerifyPassword(password, p.PasswordHash)) { SetError("Password incorrect."); return RedirectToAction("ManageTwoFactor"); }
                p.IsTwoFactorEnabled = false; p.TwoFactorSecretKey = null; p.TwoFactorRecoveryCodes = null;
                await _context.SaveChangesAsync();
                SetSuccess("2FA disabled.");
                return RedirectToAction("ManageTwoFactor");
            }
            else
            {
                var e = await _context.Employees.FindAsync(userId.Value);
                if (e == null) return RedirectToAction("Login");
                if (!VerifyPassword(password, e.PasswordHash)) { SetError("Password incorrect."); return RedirectToAction("ManageTwoFactor"); }
                e.IsTwoFactorEnabled = false; e.TwoFactorSecretKey = null; e.TwoFactorRecoveryCodes = null;
                await _context.SaveChangesAsync();
                SetSuccess("2FA disabled.");
                return RedirectToAction("ManageTwoFactor");
            }
        }

        // ======================================================================
        //  DEVICE MANAGEMENT
        // ======================================================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ManageDevices()
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();
            var userType = role == UserRole.PATIENT.ToString() ? "Patient" : "Employee";
            var devices = await _context.UserDevices
                .Where(d => d.UserId == userId && d.UserType == userType)
                .OrderByDescending(d => d.LastSeen).ToListAsync();
            return View(devices);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeDevice(int deviceId)
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();
            var userType = role == UserRole.PATIENT.ToString() ? "Patient" : "Employee";
            var dev = await _context.UserDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId && d.UserType == userType);
            if (dev != null) { _context.UserDevices.Remove(dev); await _context.SaveChangesAsync(); SetSuccess("Device removed."); }
            return RedirectToAction("ManageDevices");
        }

        // ======================================================================
        //  FORGOT / RESET PASSWORD
        // ======================================================================
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var emp = await _context.Employees.FirstOrDefaultAsync(e => e.Email == model.Email);
            var pat = await _context.Patients.FirstOrDefaultAsync(p => p.Email == model.Email);

            if (emp == null && pat == null)
            {
                SetSuccess("If an account exists, a reset link has been sent.");
                return RedirectToAction("ForgotPasswordConfirmation");
            }

            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            DateTime expiry = DateTime.Now.AddHours(1);

            if (emp != null)
            {
                emp.ResetToken = token;
                emp.ResetTokenExpiry = expiry;
                await _context.SaveChangesAsync();

                var link = Url.Action("ResetPassword", "Account", new { email = emp.Email, token }, Request.Scheme)!;
                await _emailService.SendPasswordResetEmailAsync(emp.Email, emp.Email, link);
            }
            else if (pat != null)
            {
                pat.ResetToken = token;
                pat.ResetTokenExpiry = expiry;
                await _context.SaveChangesAsync();

                var link = Url.Action("ResetPassword", "Account", new { email = pat.Email, token }, Request.Scheme)!;
                await _emailService.SendPasswordResetEmailAsync(pat.Email, pat.Email, link);
            }

            SetSuccess("If an account exists, a reset link has been sent.");
            return RedirectToAction("ForgotPasswordConfirmation");
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation() => View();

        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token)) return RedirectToAction("Login");
            return View(new ResetPasswordViewModel { Email = email, Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            if (!IsPasswordComplex(model.Password))
            {
                SetError("Password must be at least 8 chars with upper, digit, special.");
                return View(model);
            }

            var emp = await _context.Employees.FirstOrDefaultAsync(e =>
                e.Email == model.Email && e.ResetToken == model.Token && e.ResetTokenExpiry > DateTime.Now);
            var pat = await _context.Patients.FirstOrDefaultAsync(p =>
                p.Email == model.Email && p.ResetToken == model.Token && p.ResetTokenExpiry > DateTime.Now);

            if (emp == null && pat == null)
            { SetError("Invalid or expired token."); return View(model); }

            if (emp != null)
            {
                if (IsPasswordPreviouslyUsed(model.Password, emp.PreviousPasswordHashes))
                {
                    SetError("You cannot reuse a previous password.");
                    return View(model);
                }

                string oldHash = emp.PasswordHash;
                emp.PasswordHash = HashPassword(model.Password);
                emp.PreviousPasswordHashes = AddPasswordToHistory(oldHash, emp.PreviousPasswordHashes);
                emp.ResetToken = null;
                emp.ResetTokenExpiry = null;
                emp.MustChangePassword = false;
            }
            else if (pat != null)
            {
                if (IsPasswordPreviouslyUsed(model.Password, pat.PreviousPasswordHashes))
                {
                    SetError("You cannot reuse a previous password.");
                    return View(model);
                }

                string oldHash = pat.PasswordHash;
                pat.PasswordHash = HashPassword(model.Password);
                pat.PreviousPasswordHashes = AddPasswordToHistory(oldHash, pat.PreviousPasswordHashes);
                pat.ResetToken = null;
                pat.ResetTokenExpiry = null;
                pat.MustChangePassword = false;
            }

            await _context.SaveChangesAsync();
            SetSuccess("Password reset. Please login.");
            return RedirectToAction("ResetPasswordConfirmation");
        }

        [HttpGet]
        public IActionResult ResetPasswordConfirmation() => View();

        // ======================================================================
        //  CHANGE PASSWORD
        // ======================================================================
        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword() => View();

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            if (!IsPasswordComplex(model.NewPassword))
            {
                SetError("Password must be at least 8 chars with upper, digit, special.");
                return View(model);
            }

            var userId = GetCurrentUserId();
            var role = GetCurrentUserRole();
            if (userId == null || string.IsNullOrEmpty(role))
            { SetError("Session expired."); return RedirectToAction("Login"); }

            string? previousHashesJson = null;
            string? currentPasswordHash = null;

            if (role == UserRole.PATIENT.ToString())
            {
                var p = await _context.Patients.FindAsync(userId.Value);
                if (p == null) return RedirectToAction("Login");
                if (!VerifyPassword(model.CurrentPassword, p.PasswordHash))
                { SetError("Current password incorrect."); return View(model); }

                if (IsPasswordPreviouslyUsed(model.NewPassword, p.PreviousPasswordHashes))
                {
                    SetError("You cannot reuse a previous password.");
                    return View(model);
                }

                currentPasswordHash = p.PasswordHash;
                previousHashesJson = p.PreviousPasswordHashes;

                p.PasswordHash = HashPassword(model.NewPassword);
                p.PreviousPasswordHashes = AddPasswordToHistory(currentPasswordHash, previousHashesJson);
                p.MustChangePassword = false;
            }
            else
            {
                var e = await _context.Employees.FindAsync(userId.Value);
                if (e == null) return RedirectToAction("Login");
                if (!VerifyPassword(model.CurrentPassword, e.PasswordHash))
                { SetError("Current password incorrect."); return View(model); }

                if (IsPasswordPreviouslyUsed(model.NewPassword, e.PreviousPasswordHashes))
                {
                    SetError("You cannot reuse a previous password.");
                    return View(model);
                }

                currentPasswordHash = e.PasswordHash;
                previousHashesJson = e.PreviousPasswordHashes;

                e.PasswordHash = HashPassword(model.NewPassword);
                e.PreviousPasswordHashes = AddPasswordToHistory(currentPasswordHash, previousHashesJson);
                e.MustChangePassword = false;
            }

            await _context.SaveChangesAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()), new Claim(ClaimTypes.Role, role) };
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties { IsPersistent = true });

            SetSuccess("Password changed.");
            return RedirectToDashboard();
        }

        // ======================================================================
        //  EDIT PROFILE
        // ======================================================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var emp = await _context.Employees.FindAsync(userId.Value);
            if (emp == null) return RedirectToAction("Login");
            return View(new EditProfileViewModel { FirstName = emp.FirstName, LastName = emp.LastName });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(EditProfileViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var emp = await _context.Employees.FindAsync(userId.Value);
            if (emp == null) return RedirectToAction("Login");
            emp.FirstName = model.FirstName; emp.LastName = model.LastName;
            await _context.SaveChangesAsync();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await SignInAsync(BuildEmployeeClaims(emp), true);
            SetSuccess("Profile updated.");
            return RedirectToDashboard(emp.Role);
        }

        // ======================================================================
        //  DEACTIVATE ACCOUNT
        // ======================================================================
        [Authorize]
        [HttpGet]
        public IActionResult DeactivateAccount() => View();

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateAccount(string password)
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();

            if (role == UserRole.PATIENT.ToString())
            {
                var p = await _context.Patients.FindAsync(userId.Value);
                if (p == null) return RedirectToAction("Login");
                if (!VerifyPassword(password, p.PasswordHash)) { SetError("Password incorrect."); return RedirectToAction("DeactivateAccount"); }
                p.IsActive = Status.Inactive;
            }
            else
            {
                var e = await _context.Employees.FindAsync(userId.Value);
                if (e == null) return RedirectToAction("Login");
                if (!VerifyPassword(password, e.PasswordHash)) { SetError("Password incorrect."); return RedirectToAction("DeactivateAccount"); }
                e.IsActive = Status.Inactive;
            }
            await _context.SaveChangesAsync();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            SetSuccess("Account deactivated.");
            return RedirectToAction("Login");
        }

        // ======================================================================
        //  LOGOUT
        // ======================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            SetSuccess("Logged out.");
            return RedirectToAction("Login");
        }

        // ======================================================================
        //  CHANGE EMAIL
        // ======================================================================
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ChangeEmail()
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();

            string? currentEmail = null;
            string? pendingEmail = null;

            if (role == UserRole.PATIENT.ToString())
            {
                var patient = await _context.Patients.FindAsync(userId.Value);
                if (patient == null) return RedirectToAction("Login");
                currentEmail = patient.Email;
                pendingEmail = patient.PendingEmail;
            }
            else
            {
                var employee = await _context.Employees.FindAsync(userId.Value);
                if (employee == null) return RedirectToAction("Login");
                currentEmail = employee.Email;
                pendingEmail = employee.PendingEmail;
            }

            return View(new ChangeEmailViewModel
            {
                CurrentEmail = currentEmail,
                NewEmail = pendingEmail ?? string.Empty
            });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeEmail(ChangeEmailViewModel model)
        {
            var userId = GetCurrentUserId(); if (userId == null) return RedirectToAction("Login");
            var role = GetCurrentUserRole();

            if (!ModelState.IsValid) return View(model);

            bool emailExists = false;
            if (role == UserRole.PATIENT.ToString())
            {
                emailExists = await _context.Patients.AnyAsync(p => p.Email == model.NewEmail && p.Id != userId.Value)
                              || await _context.Employees.AnyAsync(e => e.Email == model.NewEmail);
            }
            else
            {
                emailExists = await _context.Employees.AnyAsync(e => e.Email == model.NewEmail && e.EmployeeID != userId.Value)
                              || await _context.Patients.AnyAsync(p => p.Email == model.NewEmail);
            }

            if (emailExists)
            {
                ModelState.AddModelError("NewEmail", "This email is already in use.");
                return View(model);
            }

            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            DateTime expiry = DateTime.Now.AddHours(24);

            string? userName = null;

            if (role == UserRole.PATIENT.ToString())
            {
                var patient = await _context.Patients.FindAsync(userId.Value);
                if (patient == null) return RedirectToAction("Login");
                patient.PendingEmail = model.NewEmail;
                patient.EmailChangeToken = token;
                patient.EmailChangeTokenExpiry = expiry;
                userName = $"{patient.FirstName} {patient.LastName}";
            }
            else
            {
                var employee = await _context.Employees.FindAsync(userId.Value);
                if (employee == null) return RedirectToAction("Login");
                employee.PendingEmail = model.NewEmail;
                employee.EmailChangeToken = token;
                employee.EmailChangeTokenExpiry = expiry;
                userName = $"{employee.FirstName} {employee.LastName}";
            }

            await _context.SaveChangesAsync();

            var confirmationLink = Url.Action("ConfirmEmailChange", "Account",
                new { email = model.NewEmail, token }, Request.Scheme)!;
            await _emailService.SendEmailChangeConfirmationAsync(model.NewEmail, userName!, confirmationLink);

            SetSuccess("A confirmation link has been sent to your new email address. Please check your inbox.");
            return RedirectToAction("ChangeEmail");
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmailChange(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                SetError("Invalid confirmation link.");
                return RedirectToAction("Login");
            }

            var patient = await _context.Patients.FirstOrDefaultAsync(p =>
                p.PendingEmail == email && p.EmailChangeToken == token && p.EmailChangeTokenExpiry > DateTime.Now);
            if (patient != null)
            {
                patient.Email = email;
                patient.PendingEmail = null;
                patient.EmailChangeToken = null;
                patient.EmailChangeTokenExpiry = null;
                await _context.SaveChangesAsync();
                SetSuccess("Your email address has been updated successfully.");
                return RedirectToAction("Login");
            }

            var employee = await _context.Employees.FirstOrDefaultAsync(e =>
                e.PendingEmail == email && e.EmailChangeToken == token && e.EmailChangeTokenExpiry > DateTime.Now);
            if (employee != null)
            {
                employee.Email = email;
                employee.PendingEmail = null;
                employee.EmailChangeToken = null;
                employee.EmailChangeTokenExpiry = null;
                await _context.SaveChangesAsync();
                SetSuccess("Your email address has been updated successfully.");
                return RedirectToAction("Login");
            }

            SetError("Invalid or expired confirmation link.");
            return RedirectToAction("Login");
        }
    }
}