using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Services
{
    public class NotificationService : INotificationService
    {
        private readonly WardDbContext _context;

        public NotificationService(WardDbContext context)
        {
            _context = context;
        }

        public async Task NotifyUserAsync(int userId, string userType, string message, string? link = null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = userId,
                UserType = userType,
                Message = message,
                Link = link,
                IsActive = Status.Active
            });
            await _context.SaveChangesAsync();
        }

        public async Task NotifyRoleAsync(string role, string message, string? link = null)
        {
            _context.Notifications.Add(new Notification
            {
                Role = role,
                Message = message,
                Link = link,
                IsActive = Status.Active
            });
            await _context.SaveChangesAsync();
        }

        public async Task NotifyAllAsync(string message, string? link = null)
        {
            _context.Notifications.Add(new Notification
            {
                Message = message,
                Link = link,
                IsActive = Status.Active
            });
            await _context.SaveChangesAsync();
        }
    }
}