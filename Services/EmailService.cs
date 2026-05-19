// Services/EmailService.cs
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
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
    }
}