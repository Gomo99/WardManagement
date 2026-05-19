using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize (Roles = "SUPPLIER")]
    public class SupplierController : Controller
    {
        private readonly WardDbContext _context;

        public SupplierController(WardDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            ViewBag.PendingOrders = await _context.ConsumableOrders
                .CountAsync(o => o.IsActive == Status.Active && o.OrderStatus == OrderStatus.Ordered);
            ViewBag.FulfilledOrders = await _context.ConsumableOrders
                .CountAsync(o => o.IsActive == Status.Active && o.OrderStatus == OrderStatus.Fulfilled);
            return View();
        }

        // ==================================================================
        //  LIST ORDERS PENDING FULFILLMENT
        // ==================================================================
        public async Task<IActionResult> Index(string status = "Ordered")
        {
            // Default to showing "Ordered" orders
            var query = _context.ConsumableOrders
                .Include(o => o.Consumable)
                .Where(o => o.IsActive == Status.Active);

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, out var parsedStatus))
            {
                query = query.Where(o => o.OrderStatus == parsedStatus);
            }
            else
            {
                query = query.Where(o => o.OrderStatus == OrderStatus.Ordered);
            }

            var orders = await query
                .OrderByDescending(o => o.RequestDate)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            return View(orders);
        }

        // ==================================================================
        //  CONFIRM FULFILLMENT – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmFulfillment(int orderId)
        {
            var order = await _context.ConsumableOrders
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                         o.OrderStatus == OrderStatus.Ordered &&
                                         o.IsActive == Status.Active);
            if (order == null) return NotFound();

            order.OrderStatus = OrderStatus.Fulfilled;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Order fulfilled. The ward can now receive the delivery.";
            return RedirectToAction("Index");
        }

        // ==================================================================
        //  ORDER DETAILS
        // ==================================================================
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == id && o.IsActive == Status.Active);
            if (order == null) return NotFound();
            return View(order);
        }
    }
}