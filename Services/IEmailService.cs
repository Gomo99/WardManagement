namespace WARDMANAGEMENTSYSTEM.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlBody);
        Task SendAsync(string toEmail, string subject, string htmlBody); // optional, keep for backward compatibility

        // Template-based methods
        Task SendEmployeeWelcomeEmailAsync(string toEmail, string firstName, string lastName,
            string email, string tempPassword, string loginUrl);
        Task SendPatientWelcomeEmailAsync(string toEmail, string firstName, string lastName,
            string email, string tempPassword, string loginUrl);
        Task SendPasswordResetEmailAsync(string toEmail, string email, string resetLink);
    }
}
