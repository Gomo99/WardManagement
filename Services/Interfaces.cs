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

        // in Services/IEmailService.cs
        Task SendEmailChangeConfirmationAsync(string toEmail, string userName, string confirmationLink);
    }


    public interface INotificationService
    {
        Task NotifyUserAsync(int userId, string userType, string message, string? link = null);
        Task NotifyRoleAsync(string role, string message, string? link = null);
        Task NotifyAllAsync(string message, string? link = null);
    }


    public interface IPdfReportService
    {

    }


    public interface IRazorViewToStringRenderer
    {
        Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model);
    }


    public interface ITwoFactorService
    {
        string GenerateSecretKey();
        string GetQrCodeUri(string secretKey, string email, string issuer);
        byte[] GenerateQrCodePng(string uri);
        bool VerifyCode(string secretKey, string code);
        List<string> GenerateRecoveryCodes();
        bool VerifyRecoveryCode(string storedJson, string inputCode, out string updatedJson);


    }




}
