namespace WARDMANAGEMENTSYSTEM.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlBody);
        Task SendAsync(string toEmail, string subject, string htmlBody); // optional, keep for backward compatibility
    }
}
