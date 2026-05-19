using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using Microsoft.AspNetCore.SignalR;
using WARDMANAGEMENTSYSTEM.Hubs;

namespace WARDMANAGEMENTSYSTEM.Services
{
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
}