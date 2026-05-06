namespace WARDMANAGEMENTSYSTEM.Services
{
    public interface INotificationService
    {
        Task NotifyUserAsync(int userId, string userType, string message, string? link = null);
        Task NotifyRoleAsync(string role, string message, string? link = null);
        Task NotifyAllAsync(string message, string? link = null);
    }
}