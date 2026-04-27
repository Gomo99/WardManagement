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
    public class AccountController : Controller
    {
        private readonly WardDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ITwoFactorService _twoFactorService;
        private const string DeviceIdCookieName = ".DeviceId";

        private const string SuccessMessageKey = "SuccessMessage";
        private const string ErrorMessageKey = "ErrorMessage";

        public AccountController(WardDbContext context, IEmailService emailService, ITwoFactorService twoFactorService)
        {
            _context = context;
            _emailService = emailService;
            _twoFactorService = twoFactorService;
        }

        // --------------------------------------------------------------------------------
        //  HELPER METHODS
        // --------------------------------------------------------------------------------
        private void SetSuccess(string message) => TempData[SuccessMessageKey] = message;
        private void SetError(string message) => TempData[ErrorMessageKey] = message;

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return null;
            return userId;
        }

        private ClaimsPrincipal BuildEmployeeClaims(Employee employee)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, employee.EmployeeID.ToString()),
                new Claim(ClaimTypes.Name, employee.Email),
                new Claim(ClaimTypes.Email, employee.Email),
                new Claim(ClaimTypes.GivenName, employee.FirstName),
                new Claim(ClaimTypes.Surname, employee.LastName),
                new Claim(ClaimTypes.Role, employee.Role.ToString())
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        private ClaimsPrincipal BuildPatientClaims(Models.Patient patient)
        {
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, patient.Id.ToString()),
        new Claim(ClaimTypes.Name, patient.Email),
        new Claim(ClaimTypes.Email, patient.Email),
        new Claim(ClaimTypes.GivenName, patient.FirstName),
        new Claim(ClaimTypes.Surname, patient.LastName),
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
                var roleStr = User.FindFirstValue(ClaimTypes.Role);
                Enum.TryParse<UserRole>(roleStr, out var parsed);
                role = parsed;
            }

            return role switch
            {
                UserRole.ADMINISTRATOR => RedirectToAction("Dashboard", "AdminController"),
                UserRole.DOCTOR => RedirectToAction("Dashboard", "DoctorController"),
                UserRole.PATIENT => RedirectToAction("Dashboard", "PatientController"),
                UserRole.NURSE => RedirectToAction("Dashboard", "NurseController"),
                UserRole.NURSINGSISTER => RedirectToAction("Dashboard", "NursingSisterController"),
                UserRole.WARDADMIN => RedirectToAction("Dashboard", "WardAdminController"),
                UserRole.SCRIPTMANAGER => RedirectToAction("Dashboard", "ScriptManagerController"),
                UserRole.CONSUMABLESMANAGER => RedirectToAction("Dashboard", "ConsumablesManagerController"),
                _ => RedirectToAction("Login", "Account")
            };
        }

        private IActionResult RedirectToSavedUrl(string? returnUrl, UserRole? role = null)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToDashboard(role);
        }

        private static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);

        private static bool VerifyAndUpgradePassword(string password, ref string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash)) return false;
            if (storedHash.StartsWith("$2"))
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            if (password == storedHash)
            {
                storedHash = BCrypt.Net.BCrypt.HashPassword(password);
                return true;
            }
            return false;
        }

        private static bool VerifyPassword(string password, string hash)
        {
            if (string.IsNullOrEmpty(hash)) return false;
            if (hash.StartsWith("$2")) return BCrypt.Net.BCrypt.Verify(password, hash);
            return password == hash;
        }

        private static bool IsPasswordComplex(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8) return false;
            bool hasUpper = Regex.IsMatch(password, @"[A-Z]");
            bool hasDigit = Regex.IsMatch(password, @"\d");
            bool hasSpecial = Regex.IsMatch(password, @"[^a-zA-Z0-9\s]");
            return hasUpper && hasDigit && hasSpecial;
        }

        private static int CountRecoveryCodes(string? json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            try { return JsonSerializer.Deserialize<List<string>>(json)?.Count ?? 0; }
            catch { return 0; }
        }

        private string GetOrCreateDeviceId()
        {
            if (Request.Cookies.TryGetValue(DeviceIdCookieName, out var existingId))
                return existingId;

            var newId = Guid.NewGuid().ToString("N");
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            };
            Response.Cookies.Append(DeviceIdCookieName, newId, cookieOptions);
            return newId;
        }

        private async Task<UserDevice> GetOrCreateDeviceRecord(int userId, string userType, string deviceId, string? ipAddress)
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
                    IpAddress = ipAddress,
                    IsTrusted = false,
                    FirstSeen = DateTime.Now,
                    LastSeen = DateTime.Now
                };
                _context.UserDevices.Add(device);
            }
            else
            {
                device.LastSeen = DateTime.Now;
                device.IpAddress = ipAddress;
            }
            await _context.SaveChangesAsync();
            return device;
        }

        // --------------------------------------------------------------------------------
        //  LOGIN
        // --------------------------------------------------------------------------------
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToDashboard();
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(model);

            // 1. Try Employee
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email == model.UserNameorEmail);

            if (employee != null)
            {
                if (employee.IsActive != Status.Active)
                {
                    SetError("Account is inactive.");
                    return View(model);
                }
                if (employee.LockoutEnd.HasValue && employee.LockoutEnd > DateTime.Now)
                {
                    SetError($"Account locked. Try again after {employee.LockoutEnd:HH:mm}.");
                    return View(model);
                }

                string passwordHash = employee.PasswordHash;
                bool passwordValid = VerifyAndUpgradePassword(model.Password, ref passwordHash);
                if (passwordHash != employee.PasswordHash)
                    employee.PasswordHash = passwordHash;

                if (!passwordValid)
                {
                    employee.FailedLoginAttempts++;
                    if (employee.FailedLoginAttempts >= 5)
                    {
                        employee.LockoutEnd = DateTime.Now.AddMinutes(15);
                        employee.FailedLoginAttempts = 0;
                        SetError("Too many failed attempts. Account locked for 15 minutes.");
                    }
                    else
                    {
                        SetError($"Invalid email or password. {5 - employee.FailedLoginAttempts} attempt(s) remaining.");
                    }
                    await _context.SaveChangesAsync();
                    return View(model);
                }

                employee.FailedLoginAttempts = 0;
                employee.LockoutEnd = null;
                await _context.SaveChangesAsync();

                string deviceId = GetOrCreateDeviceId();
                var device = await GetOrCreateDeviceRecord(employee.EmployeeID, "Employee", deviceId,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                if (employee.IsTwoFactorEnabled && !string.IsNullOrEmpty(employee.TwoFactorSecretKey))
                {
                    if (device.IsTrusted)
                    {
                        await SignInAsync(BuildEmployeeClaims(employee), model.RememberMe);
                        if (employee.MustChangePassword)
                            return RedirectToAction("ChangePassword");
                        return RedirectToSavedUrl(returnUrl, employee.Role);
                    }
                    TempData["2fa_pending_id"] = employee.EmployeeID.ToString();
                    TempData["2fa_pending_type"] = "Employee";
                    TempData["2fa_remember_me"] = model.RememberMe.ToString();
                    TempData["2fa_return_url"] = returnUrl ?? string.Empty;
                    return RedirectToAction("TwoFactorChallenge");
                }

                await SignInAsync(BuildEmployeeClaims(employee), model.RememberMe);
                if (employee.MustChangePassword)
                    return RedirectToAction("ChangePassword");
                return RedirectToSavedUrl(returnUrl, employee.Role);
            }

            // 2. Try Patient
            var patient = await _context.Patients
                .FirstOrDefaultAsync(p => p.Email == model.UserNameorEmail);

            if (patient != null)
            {
                if (patient.IsActive != Status.Active)
                {
                    SetError("Account is inactive.");
                    return View(model);
                }
                if (patient.LockoutEnd.HasValue && patient.LockoutEnd > DateTime.Now)
                {
                    SetError($"Account locked. Try again after {patient.LockoutEnd:HH:mm}.");
                    return View(model);
                }

                string patientPasswordHash = patient.PasswordHash;
                bool passwordValid = VerifyAndUpgradePassword(model.Password, ref patientPasswordHash);
                if (patientPasswordHash != patient.PasswordHash)
                    patient.PasswordHash = patientPasswordHash;

                if (!passwordValid)
                {
                    patient.FailedLoginAttempts++;
                    if (patient.FailedLoginAttempts >= 5)
                    {
                        patient.LockoutEnd = DateTime.Now.AddMinutes(15);
                        patient.FailedLoginAttempts = 0;
                        SetError("Too many failed attempts. Account locked for 15 minutes.");
                    }
                    else
                    {
                        SetError($"Invalid email or password. {5 - patient.FailedLoginAttempts} attempt(s) remaining.");
                    }
                    await _context.SaveChangesAsync();
                    return View(model);
                }

                patient.FailedLoginAttempts = 0;
                patient.LockoutEnd = null;
                await _context.SaveChangesAsync();

                await SignInAsync(BuildPatientClaims(patient), model.RememberMe);
                if (patient.MustChangePassword)
                    return RedirectToAction("ChangePassword");
                return RedirectToSavedUrl(returnUrl, UserRole.PATIENT);
            }

            SetError("Invalid email or password.");
            return View(model);
        }

        // --------------------------------------------------------------------------------
        //  TWO-FACTOR CHALLENGE
        // --------------------------------------------------------------------------------
        [HttpGet]
        public IActionResult TwoFactorChallenge()
        {
            if (TempData["2fa_pending_id"] == null)
                return RedirectToAction("Login");
            var model = new TwoFactorChallengeViewModel
            {
                ReturnUrl = TempData["2fa_return_url"]?.ToString() ?? string.Empty
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TwoFactorChallenge(TwoFactorChallengeViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var pendingId = TempData["2fa_pending_id"]?.ToString();
            var pendingType = TempData["2fa_pending_type"]?.ToString();
            var rememberMe = TempData["2fa_remember_me"]?.ToString() == "True";
            var returnUrl = TempData["2fa_return_url"]?.ToString();

            if (string.IsNullOrEmpty(pendingId) || pendingType != "Employee")
                return RedirectToAction("Login");
            if (!int.TryParse(pendingId, out int employeeId))
                return RedirectToAction("Login");

            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null) return RedirectToAction("Login");

            bool isValid = false;
            if (model.UseRecoveryCode)
            {
                if (!string.IsNullOrEmpty(model.RecoveryCode))
                {
                    isValid = _twoFactorService.VerifyRecoveryCode(
                        employee.TwoFactorRecoveryCodes ?? string.Empty,
                        model.RecoveryCode,
                        out string updatedJson);
                    if (isValid) employee.TwoFactorRecoveryCodes = updatedJson;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(model.Code))
                    isValid = _twoFactorService.VerifyCode(employee.TwoFactorSecretKey!, model.Code);
            }

            if (!isValid)
            {
                SetError("Invalid authentication code. Please try again.");
                TempData["2fa_pending_id"] = employee.EmployeeID.ToString();
                TempData["2fa_pending_type"] = "Employee";
                TempData["2fa_remember_me"] = rememberMe.ToString();
                TempData["2fa_return_url"] = returnUrl ?? string.Empty;
                return View(model);
            }

            if (model.TrustDevice)
            {
                string deviceId = GetOrCreateDeviceId();
                var device = await _context.UserDevices
                    .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.UserId == employeeId && d.UserType == "Employee");
                if (device != null) device.IsTrusted = true;
            }

            await _context.SaveChangesAsync();
            await SignInAsync(BuildEmployeeClaims(employee), rememberMe);

            TempData.Remove("2fa_pending_id");
            TempData.Remove("2fa_pending_type");
            TempData.Remove("2fa_remember_me");
            TempData.Remove("2fa_return_url");

            if (employee.MustChangePassword)
                return RedirectToAction("ChangePassword");
            return RedirectToSavedUrl(returnUrl, employee.Role);
        }

        // --------------------------------------------------------------------------------
        //  TWO-FACTOR SETUP & MANAGEMENT
        // --------------------------------------------------------------------------------
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> SetupTwoFactor()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");
            var employee = await _context.Employees.FindAsync(userId.Value);
            if (employee == null || employee.IsTwoFactorEnabled) return RedirectToAction("ManageTwoFactor");

            var secretKey = _twoFactorService.GenerateSecretKey();
            employee.TwoFactorSecretKey = secretKey;
            await _context.SaveChangesAsync();

            var uri = _twoFactorService.GetQrCodeUri(secretKey, employee.Email, "NMB-HLabSys");
            var qrBytes = _twoFactorService.GenerateQrCodePng(uri);
            var qrBase64 = Convert.ToBase64String(qrBytes);

            return View(new TwoFactorSetupViewModel
            {
                SecretKey = secretKey,
                QrCodeBase64 = qrBase64
            });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetupTwoFactor(TwoFactorSetupViewModel model)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");
            var employee = await _context.Employees.FindAsync(userId.Value);
            if (employee == null) return RedirectToAction("Login");

            if (!ModelState.IsValid)
            {
                var uri = _twoFactorService.GetQrCodeUri(employee.TwoFactorSecretKey!, employee.Email, "NMB-HLabSys");
                var qrBytes = _twoFactorService.GenerateQrCodePng(uri);
                model.QrCodeBase64 = Convert.ToBase64String(qrBytes);
                model.SecretKey = employee.TwoFactorSecretKey!;
                return View(model);
            }

            if (!_twoFactorService.VerifyCode(employee.TwoFactorSecretKey!, model.VerificationCode))
            {
                SetError("Invalid verification code. Please try again.");
                var uri = _twoFactorService.GetQrCodeUri(employee.TwoFactorSecretKey!, employee.Email, "NMB-HLabSys");
                var qrBytes = _twoFactorService.GenerateQrCodePng(uri);
                model.QrCodeBase64 = Convert.ToBase64String(qrBytes);
                model.SecretKey = employee.TwoFactorSecretKey!;
                return View(model);
            }

            employee.IsTwoFactorEnabled = true;
            var recoveryCodes = _twoFactorService.GenerateRecoveryCodes();
            var hashedCodes = recoveryCodes.Select(c => HashPassword(c)).ToList();
            employee.TwoFactorRecoveryCodes = JsonSerializer.Serialize(hashedCodes);
            await _context.SaveChangesAsync();

            TempData["ShowRecoveryCodes"] = "true";
            return RedirectToAction("ShowRecoveryCodes", new { codes = string.Join(",", recoveryCodes) });
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
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");
            var employee = await _context.Employees.FindAsync(userId.Value);
            if (employee == null) return RedirectToAction("Login");
            return View(new ManageTwoFactorViewModel
            {
                IsTwoFactorEnabled = employee.IsTwoFactorEnabled,
                RecoveryCodesLeft = CountRecoveryCodes(employee.TwoFactorRecoveryCodes)
            });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegenerateRecoveryCodes()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");
            var employee = await _context.Employees.FindAsync(userId.Value);
            if (employee == null || !employee.IsTwoFactorEnabled) return RedirectToAction("SetupTwoFactor");

            var recoveryCodes = _twoFactorService.GenerateRecoveryCodes();
            var hashedCodes = recoveryCodes.Select(c => HashPassword(c)).ToList();
            employee.TwoFactorRecoveryCodes = JsonSerializer.Serialize(hashedCodes);
            await _context.SaveChangesAsync();

            TempData["ShowRecoveryCodes"] = "true";
            return RedirectToAction("ShowRecoveryCodes", new { codes = string.Join(",", recoveryCodes) });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisableTwoFactor(string password)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");
            var employee = await _context.Employees.FindAsync(userId.Value);
            if (employee == null) return RedirectToAction("Login");

            if (!VerifyPassword(password, employee.PasswordHash))
            {
                SetError("Password is incorrect.");
                return RedirectToAction("ManageTwoFactor");
            }
            employee.IsTwoFactorEnabled = false;
            employee.TwoFactorSecretKey = null;
            employee.TwoFactorRecoveryCodes = null;
            await _context.SaveChangesAsync();
            SetSuccess("Two-factor authentication has been disabled.");
            return RedirectToAction("ManageTwoFactor");
        }

        // --------------------------------------------------------------------------------
        //  DEVICE MANAGEMENT
        // --------------------------------------------------------------------------------
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ManageDevices()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");

            var userType = User.FindFirstValue(ClaimTypes.Role) == UserRole.PATIENT.ToString() ? "Patient" : "Employee";
            var devices = await _context.UserDevices
                .Where(d => d.UserId == userId && d.UserType == userType)
                .OrderByDescending(d => d.LastSeen)
                .ToListAsync();
            return View(devices);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeDevice(int deviceId)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");

            var userType = User.FindFirstValue(ClaimTypes.Role) == UserRole.PATIENT.ToString() ? "Patient" : "Employee";
            var device = await _context.UserDevices
                .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId && d.UserType == userType);
            if (device != null)
            {
                _context.UserDevices.Remove(device);
                await _context.SaveChangesAsync();
                SetSuccess("Device removed.");
            }
            return RedirectToAction("ManageDevices");
        }

        // --------------------------------------------------------------------------------
        //  FORGOT / RESET PASSWORD
        // --------------------------------------------------------------------------------
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Email == model.Email);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.Email == model.Email);

            if (employee == null && patient == null)
            {
                SetSuccess("If an account exists, a password reset link has been sent.");
                return RedirectToAction(nameof(ForgotPasswordConfirmation));
            }

            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            DateTime expiry = DateTime.Now.AddHours(1);

            if (employee != null)
            {
                employee.ResetToken = token;
                employee.ResetTokenExpiry = expiry;
                await _context.SaveChangesAsync();
                string resetLink = Url.Action("ResetPassword", "Account", new { email = employee.Email, token }, Request.Scheme)!;
                await _emailService.SendEmailAsync(employee.Email, "Password Reset Request",
                    $"Please reset your password by clicking this link: {resetLink}");
            }
            else if (patient != null)
            {
                patient.ResetToken = token;
                patient.ResetTokenExpiry = expiry;
                await _context.SaveChangesAsync();
                string resetLink = Url.Action("ResetPassword", "Account", new { email = patient.Email, token }, Request.Scheme)!;
                await _emailService.SendEmailAsync(patient.Email, "Password Reset Request",
                    $"Please reset your password by clicking this link: {resetLink}");
            }

            SetSuccess("If an account exists, a password reset link has been sent.");
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation() => View();

        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
                return RedirectToAction("Login");
            return View(new ResetPasswordViewModel { Email = email, Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);
            if (!IsPasswordComplex(model.Password))
            {
                SetError("Password must be at least 8 characters and contain an uppercase letter, a number, and a special character.");
                return View(model);
            }

            var employee = await _context.Employees.FirstOrDefaultAsync(e =>
                e.Email == model.Email && e.ResetToken == model.Token && e.ResetTokenExpiry > DateTime.Now);
            var patient = await _context.Patients.FirstOrDefaultAsync(p =>
                p.Email == model.Email && p.ResetToken == model.Token && p.ResetTokenExpiry > DateTime.Now);

            if (employee == null && patient == null)
            {
                SetError("Invalid or expired token.");
                return View(model);
            }

            if (employee != null)
            {
                employee.PasswordHash = HashPassword(model.Password);
                employee.ResetToken = null;
                employee.ResetTokenExpiry = null;
                employee.MustChangePassword = false;
            }
            else if (patient != null)
            {
                patient.PasswordHash = HashPassword(model.Password);
                patient.ResetToken = null;
                patient.ResetTokenExpiry = null;
                patient.MustChangePassword = false;
            }
            await _context.SaveChangesAsync();
            SetSuccess("Your password has been reset successfully. Please login.");
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        [HttpGet]
        public IActionResult ResetPasswordConfirmation() => View();

        // --------------------------------------------------------------------------------
        //  CHANGE PASSWORD
        // --------------------------------------------------------------------------------
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
                SetError("Password must be at least 8 characters and contain an uppercase letter, a number, and a special character.");
                return View(model);
            }

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                SetError("Session expired. Please login again.");
                return RedirectToAction("Login");
            }

            if (roleClaim == UserRole.PATIENT.ToString())
            {
                var patient = await _context.Patients.FindAsync(userId);
                if (patient == null) return RedirectToAction("Login");
                if (!VerifyPassword(model.CurrentPassword, patient.PasswordHash))
                {
                    SetError("Current password is incorrect.");
                    return View(model);
                }
                patient.PasswordHash = HashPassword(model.NewPassword);
                patient.MustChangePassword = false;
            }
            else
            {
                var employee = await _context.Employees.FindAsync(userId);
                if (employee == null) return RedirectToAction("Login");
                if (!VerifyPassword(model.CurrentPassword, employee.PasswordHash))
                {
                    SetError("Current password is incorrect.");
                    return View(model);
                }
                employee.PasswordHash = HashPassword(model.NewPassword);
                employee.MustChangePassword = false;
            }
            await _context.SaveChangesAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, roleClaim ?? "")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });

            SetSuccess("Password changed successfully.");
            return RedirectToDashboard();
        }

        // --------------------------------------------------------------------------------
        //  EDIT PROFILE  (UPDATED – CORRECT ROLES)
        // --------------------------------------------------------------------------------
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");

            var employee = await _context.Employees.FindAsync(userId.Value);
            if (employee == null) return RedirectToAction("Login");

            var model = new EditProfileViewModel
            {
                FirstName = employee.FirstName,
                LastName = employee.LastName
            };

            return View(model);
        }





        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(EditProfileViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");

            var employee = await _context.Employees.FindAsync(userId.Value);
            if (employee == null) return RedirectToAction("Login");

            employee.FirstName = model.FirstName;
            employee.LastName = model.LastName;
            await _context.SaveChangesAsync();

            // Re-sign in to update the cookie claims with the new name
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await SignInAsync(BuildEmployeeClaims(employee), isPersistent: true);

            SetSuccess("Profile updated successfully.");
            return RedirectToDashboard(employee.Role);
        }

        // --------------------------------------------------------------------------------
        //  DEACTIVATE ACCOUNT (NEW)
        // --------------------------------------------------------------------------------
        [Authorize]
        [HttpGet]
        public IActionResult DeactivateAccount()
        {
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateAccount(string password)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToAction("Login");

            var roleClaim = User.FindFirstValue(ClaimTypes.Role);

            if (roleClaim == UserRole.PATIENT.ToString())
            {
                var patient = await _context.Patients.FindAsync(userId);
                if (patient == null) return RedirectToAction("Login");
                if (!VerifyPassword(password, patient.PasswordHash))
                {
                    SetError("Password is incorrect.");
                    return RedirectToAction("DeactivateAccount");
                }
                patient.IsActive = Status.Inactive;
            }
            else
            {
                var employee = await _context.Employees.FindAsync(userId);
                if (employee == null) return RedirectToAction("Login");
                if (!VerifyPassword(password, employee.PasswordHash))
                {
                    SetError("Password is incorrect.");
                    return RedirectToAction("DeactivateAccount");
                }
                employee.IsActive = Status.Inactive;
            }

            await _context.SaveChangesAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();

            SetSuccess("Your account has been deactivated. Goodbye!");
            return RedirectToAction("Login");
        }

        // --------------------------------------------------------------------------------
        //  LOGOUT
        // --------------------------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();
            SetSuccess("You have been logged out successfully.");
            return RedirectToAction("Login");
        }
    }
}