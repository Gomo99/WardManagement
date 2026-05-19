using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    public class NotificationController : Controller
    {
        private readonly WardDbContext _context;

        public NotificationController(WardDbContext context)
        {
            _context = context;
        }

        // Full list page
        public async Task<IActionResult> Index()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            var userType = roleClaim == "PATIENT" ? "Patient" : "Employee";

            var notifications = await _context.Notifications
                .Where(n => n.IsActive == Status.Active &&
                            ((n.UserId == userId && n.UserType == userType) ||
                             (n.Role == roleClaim) ||
                             (n.UserId == null && n.Role == null)))
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            return View(notifications);
        }

        // API endpoint for the dropdown – returns latest 10
        [HttpGet]
        public async Task<IActionResult> Latest()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return Json(Array.Empty<object>());

            var userType = roleClaim == "PATIENT" ? "Patient" : "Employee";

            var notifications = await _context.Notifications
                .Where(n => n.IsActive == Status.Active &&
                            ((n.UserId == userId && n.UserType == userType) ||
                             (n.Role == roleClaim) ||
                             (n.UserId == null && n.Role == null)))
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .Select(n => new
                {
                    id = n.Id,
                    message = n.Message,
                    link = n.Link,
                    isRead = n.IsRead,
                    createdAt = n.CreatedAt
                })
                .ToListAsync();

            return Json(notifications);
        }

        // Mark a single notification as read
        [HttpPost]
        public async Task<IActionResult> MarkRead(int id)
        {
            var n = await _context.Notifications.FindAsync(id);

            if (n != null)
            {
                n.IsRead = true;
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }


        [HttpPost]
        public async Task<IActionResult> MarkAllRead()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            var userType = roleClaim == "PATIENT" ? "Patient" : "Employee";

            var unreadNotifications = await _context.Notifications
                .Where(n => n.IsActive == Status.Active &&
                            !n.IsRead &&
                            ((n.UserId == userId && n.UserType == userType) ||
                             (n.Role == roleClaim) ||
                             (n.UserId == null && n.Role == null)))
                .ToListAsync();

            foreach (var notification in unreadNotifications)
            {
                notification.IsRead = true;
            }
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "All notifications marked as read.";
            return RedirectToAction(nameof(Index));
        }


        // Deactivate a single notification
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            var userType = roleClaim == "PATIENT" ? "Patient" : "Employee";

            // Only allow the owner (or role‑targeted) to deactivate the notification
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.IsActive == Status.Active &&
                        ((n.UserId == userId && n.UserType == userType) ||
                         (n.Role == roleClaim) ||
                         (n.UserId == null && n.Role == null)));

            if (notification != null)
            {
                notification.IsActive = Status.Inactive;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Notification removed.";
            }

            return RedirectToAction(nameof(Index));
        }

        // Deactivate all active notifications for the current user
        [HttpPost]
        public async Task<IActionResult> ClearAll()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return RedirectToAction("Login", "Account");

            var userType = roleClaim == "PATIENT" ? "Patient" : "Employee";

            var activeNotifications = await _context.Notifications
                .Where(n => n.IsActive == Status.Active &&
                            ((n.UserId == userId && n.UserType == userType) ||
                             (n.Role == roleClaim) ||
                             (n.UserId == null && n.Role == null)))
                .ToListAsync();

            foreach (var n in activeNotifications)
                n.IsActive = Status.Inactive;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "All notifications cleared.";
            return RedirectToAction(nameof(Index));
        }


    }
}