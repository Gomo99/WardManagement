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
            return Ok();
        }
    }
}