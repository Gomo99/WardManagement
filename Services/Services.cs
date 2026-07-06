using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MimeKit;
using OtpNet;
using QRCoder;
using QuestPDF.Infrastructure;
using System.Text.Json;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Hubs;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.ViewModel;

namespace WARDMANAGEMENTSYSTEM.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly IRazorViewToStringRenderer _razorRenderer;

        public EmailService(IConfiguration config, IRazorViewToStringRenderer razorRenderer)
        {
            _config = config;
            _razorRenderer = razorRenderer;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var host = _config["Email:Host"];
            var port = int.Parse(_config["Email:Port"]);
            var username = _config["Email:Username"];
            var password = _config["Email:Password"];
            var from = _config["Email:SenderEmail"];
            var senderName = _config["Email:SenderName"];

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(senderName, from));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };

            email.Body = bodyBuilder.ToMessageBody();

            try
            {
                using var smtp = new SmtpClient();
                smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;
                await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(username, password);
                await smtp.SendAsync(email);
                await smtp.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("EMAIL ERROR: " + ex.Message);
                throw;
            }
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            await SendEmailAsync(toEmail, subject, htmlBody);
        }

        public async Task SendEmployeeWelcomeEmailAsync(string toEmail, string firstName, string lastName,
            string email, string tempPassword, string loginUrl)
        {
            var model = new EmployeeWelcomeEmailViewModel
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                TempPassword = tempPassword,
                LoginUrl = loginUrl
            };

            var htmlBody = await _razorRenderer.RenderViewToStringAsync("Emails/EmployeeWelcome", model);
            await SendEmailAsync(toEmail, "Your Ward Management System Account", htmlBody);
        }

        public async Task SendPatientWelcomeEmailAsync(string toEmail, string firstName, string lastName,
            string email, string tempPassword, string loginUrl)
        {
            var model = new PatientWelcomeEmailViewModel
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                TempPassword = tempPassword,
                LoginUrl = loginUrl
            };

            var htmlBody = await _razorRenderer.RenderViewToStringAsync("Emails/PatientWelcome", model);
            await SendEmailAsync(toEmail, "Welcome to Our Hospital – Your Patient Account", htmlBody);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string email, string resetLink)
        {
            var model = new PasswordResetEmailViewModel
            {
                Email = email,
                ResetLink = resetLink
            };

            var htmlBody = await _razorRenderer.RenderViewToStringAsync("Emails/PasswordReset", model);
            await SendEmailAsync(toEmail, "Password Reset", htmlBody);
        }


        public async Task SendEmailChangeConfirmationAsync(string toEmail, string userName, string confirmationLink)
        {
            var subject = "Confirm your new email address";
            var body = $@"
<p>Hello {userName},</p>
<p>We received a request to change your account email to this address. Click the link below to confirm the change:</p>
<p><a href='{confirmationLink}'>{confirmationLink}</a></p>
<p>If you did not request this change, please ignore this email.</p>";

            // Use your existing email sending mechanism (e.g., SMTP)
            await SendEmailAsync(toEmail, subject, body);
        }
    }



    public class NotificationService : INotificationService
    {
        private readonly WardDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(WardDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task NotifyUserAsync(int userId, string userType, string message, string? link = null)
        {
            var notification = new Notification
            {
                UserId = userId,
                UserType = userType,
                Message = message,
                Link = link,
                IsActive = Status.Active
            };
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Real‑time: send to the specific user group
            var payload = new { id = notification.Id, message, link, isRead = false, createdAt = notification.CreatedAt };
            await _hubContext.Clients.Group($"user-{userId}").SendAsync("NewNotification", payload);
        }

        public async Task NotifyRoleAsync(string role, string message, string? link = null)
        {
            var notification = new Notification
            {
                Role = role,
                Message = message,
                Link = link,
                IsActive = Status.Active
            };
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            var payload = new { id = notification.Id, message, link, isRead = false, createdAt = notification.CreatedAt };
            await _hubContext.Clients.Group($"role-{role}").SendAsync("NewNotification", payload);
        }

        public async Task NotifyAllAsync(string message, string? link = null)
        {
            var notification = new Notification
            {
                Message = message,
                Link = link,
                IsActive = Status.Active
            };
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            var payload = new { id = notification.Id, message, link, isRead = false, createdAt = notification.CreatedAt };
            await _hubContext.Clients.Group("all").SendAsync("NewNotification", payload);
        }
    }



    public class PdfReportService : IPdfReportService
    {
        private readonly WardDbContext _context;

        public PdfReportService(WardDbContext context)
        {
            _context = context;
            QuestPDF.Settings.License = LicenseType.Community;
        }


    }



    public class RazorViewToStringRenderer : IRazorViewToStringRenderer
    {
        private readonly IRazorViewEngine _razorViewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IServiceProvider _serviceProvider;

        public RazorViewToStringRenderer(
            IRazorViewEngine razorViewEngine,
            ITempDataProvider tempDataProvider,
            IServiceProvider serviceProvider)
        {
            _razorViewEngine = razorViewEngine;
            _tempDataProvider = tempDataProvider;
            _serviceProvider = serviceProvider;
        }

        public async Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model)
        {
            var httpContext = new DefaultHttpContext { RequestServices = _serviceProvider };
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

            using (var sw = new StringWriter())
            {
                var viewResult = _razorViewEngine.FindView(actionContext, viewName, false);

                if (viewResult.View == null)
                {
                    throw new ArgumentNullException($"{viewName} does not match any available view");
                }

                var viewDictionary = new ViewDataDictionary<TModel>(
                    new EmptyModelMetadataProvider(),
                    new ModelStateDictionary())
                {
                    Model = model
                };

                var viewContext = new ViewContext(
                    actionContext,
                    viewResult.View,
                    viewDictionary,
                    new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
                    sw,
                    new HtmlHelperOptions()
                );

                await viewResult.View.RenderAsync(viewContext);
                return sw.ToString();
            }
        }
    }


    public class RecaptchaOptions
    {
        public string SiteKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }

    public class ReCaptchaService
    {
        private readonly HttpClient _httpClient;
        private readonly RecaptchaOptions _options;
        public string SiteKey => _options.SiteKey;
        public ReCaptchaService(HttpClient httpClient, IOptions<RecaptchaOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;
        }

        public async Task<bool> ValidateTokenAsync(string token)
        {
            var response = await _httpClient.PostAsync(
                "https://www.google.com/recaptcha/api/siteverify",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("secret", _options.SecretKey),
                    new KeyValuePair<string, string>("response", token)
                }));

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("success").GetBoolean();
        }
    }



    public class TwoFactorService : ITwoFactorService
    {
        public string GenerateSecretKey()
        {
            var key = KeyGeneration.GenerateRandomKey(20);
            return Base32Encoding.ToString(key);
        }

        public string GetQrCodeUri(string secretKey, string email, string issuer)
        {
            // otpauth://totp/{issuer}:{email}?secret={key}&issuer={issuer}
            var encodedIssuer = Uri.EscapeDataString(issuer);
            var encodedEmail = Uri.EscapeDataString(email);
            return $"otpauth://totp/{encodedIssuer}:{encodedEmail}" +
                   $"?secret={secretKey}&issuer={encodedIssuer}&digits=6&period=30";
        }

        public byte[] GenerateQrCodePng(string uri)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(6);
        }

        public bool VerifyCode(string secretKey, string code)
        {
            try
            {
                var keyBytes = Base32Encoding.ToBytes(secretKey);
                var totp = new Totp(keyBytes);

                // Allow 1 step of clock drift in each direction
                return totp.VerifyTotp(
                    code.Trim(),
                    out _,
                    new VerificationWindow(previous: 1, future: 1));
            }
            catch
            {
                return false;
            }
        }

        public List<string> GenerateRecoveryCodes()
        {
            var rng = new Random();
            var codes = new List<string>();

            for (int i = 0; i < 8; i++)
            {
                // Format: XXXX-XXXX  (8 hex chars)
                var part1 = rng.Next(0x1000, 0xFFFF).ToString("X4");
                var part2 = rng.Next(0x1000, 0xFFFF).ToString("X4");
                codes.Add($"{part1}-{part2}");
            }

            return codes;
        }

        public bool VerifyRecoveryCode(string storedJson, string inputCode,
                                        out string updatedJson)
        {
            updatedJson = storedJson;

            var codes = JsonSerializer.Deserialize<List<string>>(storedJson)
                        ?? new List<string>();

            // Recovery codes are stored as BCrypt hashes
            var matched = codes.FirstOrDefault(c =>
                BCrypt.Net.BCrypt.Verify(inputCode.Trim().ToUpper(), c));

            if (matched == null) return false;

            // Remove the used code (one-time use)
            codes.Remove(matched);
            updatedJson = JsonSerializer.Serialize(codes);
            return true;
        }
    }


}
