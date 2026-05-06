using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;

namespace WARDMANAGEMENTSYSTEM.Components
{
    public class NotificationBellViewComponent : ViewComponent
    {
        private readonly WardDbContext _context;

        public NotificationBellViewComponent(WardDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            if (!(User.Identity?.IsAuthenticated ?? false))
                return Content(string.Empty);

            var claimsPrincipal = User as ClaimsPrincipal;
            var userIdClaim = claimsPrincipal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = claimsPrincipal?.FindFirstValue(ClaimTypes.Role);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return Content(string.Empty);

            var userType = roleClaim == "PATIENT" ? "Patient" : "Employee";

            var unreadCount = await _context.Notifications
                .CountAsync(n => n.IsActive == Status.Active && !n.IsRead &&
                                 ((n.UserId == userId && n.UserType == userType) ||
                                  (n.Role == roleClaim) ||
                                  (n.UserId == null && n.Role == null)));

            ViewBag.UnreadCount = unreadCount;
            return View();
        }
    }
}