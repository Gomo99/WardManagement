using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace WARDMANAGEMENTSYSTEM.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
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

            // From
            email.From.Add(new MailboxAddress(senderName, from));

            // To
            email.To.Add(MailboxAddress.Parse(toEmail));

            // Subject
            email.Subject = subject;

            // Body (HTML)
            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };

            email.Body = bodyBuilder.ToMessageBody();

            try
            {
                using var smtp = new SmtpClient();

                // IMPORTANT: bypass certificate issues if needed
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

        // backward compatibility
        public async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            await SendEmailAsync(toEmail, subject, htmlBody);
        }
    }
}