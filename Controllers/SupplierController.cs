using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;
using WARDMANAGEMENTSYSTEM.Services;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "SUPPLIER")]
    // [Route("[controller]")]   <-- removed
    public class SupplierController : Controller
    {
        private readonly WardDbContext _context;
        private readonly INotificationService _notifService;

        public SupplierController(WardDbContext context, INotificationService notifService)
        {
            _context = context;
            _notifService = notifService;
        }

        private int? GetCurrentSupplierId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.SUPPLIER.ToString())
                return null;
            return id;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            ViewBag.PendingOrders = await _context.ConsumableOrders
                .CountAsync(o => o.SupplierId == supplierId && o.IsActive == Status.Active && o.OrderStatus == OrderStatus.Ordered);
            ViewBag.FulfilledOrders = await _context.ConsumableOrders
                .CountAsync(o => o.SupplierId == supplierId && o.IsActive == Status.Active && o.OrderStatus == OrderStatus.Fulfilled);
            ViewBag.UrgentOrders = await _context.ConsumableOrders
                .CountAsync(o => o.SupplierId == supplierId && o.IsActive == Status.Active && o.OrderStatus == OrderStatus.Ordered && o.IsUrgent);
            return View();
        }

        // ==================================================================
        //  LIST ORDERS PENDING FULFILLMENT
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> Index(string status = "Ordered")
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            var query = _context.ConsumableOrders
                .Include(o => o.Consumable)
                .Where(o => o.SupplierId == supplierId && o.IsActive == Status.Active);

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, out var parsedStatus))
                query = query.Where(o => o.OrderStatus == parsedStatus);
            else
                query = query.Where(o => o.OrderStatus == OrderStatus.Ordered);

            var orders = await query
                .OrderByDescending(o => o.RequestDate)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            return View(orders);
        }

        // ==================================================================
        //  CONFIRM FULFILLMENT – GET (enter estimated delivery date)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ConfirmFulfillmentForm(int orderId)
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                         o.IsActive == Status.Active &&
                                         o.OrderStatus == OrderStatus.Ordered);
            if (order == null) return NotFound();

            return View(order);
        }

        // ==================================================================
        //  CONFIRM FULFILLMENT – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmFulfillment(int orderId, DateTime? estimatedDeliveryDate,
            string? shippingReference, string? courierName, string? trackingLink,
            string? batchNumbers)
        {
            var order = await _context.ConsumableOrders
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                         o.OrderStatus == OrderStatus.Ordered &&
                                         o.IsActive == Status.Active);
            if (order == null) return NotFound();

            order.OrderStatus = OrderStatus.Fulfilled;
            order.EstimatedDeliveryDate = estimatedDeliveryDate;
            order.ShippingReference = shippingReference;
            order.CourierName = courierName;
            order.TrackingLink = trackingLink;

            if (!string.IsNullOrWhiteSpace(batchNumbers))
            {
                var batches = batchNumbers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                          .Select(b => b.Trim())
                                          .Where(b => !string.IsNullOrEmpty(b))
                                          .Distinct();

                foreach (var batch in batches)
                {
                    _context.ConsumableOrderBatches.Add(new ConsumableOrderBatch
                    {
                        ConsumableOrderId = orderId,
                        BatchNumber = batch
                    });
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Order fulfilled. Shipping and batch details recorded.";
            return RedirectToAction("Index");
        }

        // ==================================================================
        //  ORDER DETAILS
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .Include(o => o.ConsumableOrderBatches)
                .FirstOrDefaultAsync(o => o.Id == id && o.IsActive == Status.Active);
            if (order == null) return NotFound();
            return View(order);
        }

        // ==================================================================
        //  PARTIAL FULFILLMENT – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PartialFulfillment(int orderId)
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                         o.IsActive == Status.Active &&
                                         (o.OrderStatus == OrderStatus.Ordered || o.OrderStatus == OrderStatus.PartiallyFulfilled));
            if (order == null) return NotFound();

            int remaining = order.QuantityRequested - (order.QuantityFulfilled ?? 0);
            ViewBag.Remaining = remaining;
            ViewBag.AlreadyFulfilled = order.QuantityFulfilled ?? 0;
            return View(order);
        }

        // ==================================================================
        //  PARTIAL FULFILLMENT – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PartialFulfillment(int orderId, int quantityShipped, string? batchNumbers)
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                 o.IsActive == Status.Active &&
                                 (o.OrderStatus == OrderStatus.Ordered || o.OrderStatus == OrderStatus.PartiallyFulfilled));
            if (order == null) return NotFound();

            int alreadyFulfilled = order.QuantityFulfilled ?? 0;
            int remaining = order.QuantityRequested - alreadyFulfilled;

            if (quantityShipped <= 0)
            {
                TempData["ErrorMessage"] = "Quantity must be greater than zero.";
                return RedirectToAction(nameof(PartialFulfillment), new { orderId });
            }

            if (quantityShipped > remaining)
            {
                TempData["ErrorMessage"] = $"You cannot ship more than {remaining} units (remaining).";
                return RedirectToAction(nameof(PartialFulfillment), new { orderId });
            }

            order.QuantityFulfilled = alreadyFulfilled + quantityShipped;

            if (order.QuantityFulfilled.Value >= order.QuantityRequested)
                order.OrderStatus = OrderStatus.Fulfilled;
            else
                order.OrderStatus = OrderStatus.PartiallyFulfilled;

            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(batchNumbers))
            {
                var batches = batchNumbers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(b => b.Trim())
                                  .Where(b => !string.IsNullOrEmpty(b))
                                  .Distinct();
                foreach (var batch in batches)
                {
                    _context.ConsumableOrderBatches.Add(new ConsumableOrderBatch
                    {
                        ConsumableOrderId = orderId,
                        BatchNumber = batch,
                        Quantity = quantityShipped
                    });
                }
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = $"Shipped {quantityShipped} unit(s). " +
                (order.OrderStatus == OrderStatus.Fulfilled ? "Order fully fulfilled." : "Order partially fulfilled.");
            return RedirectToAction("Index");
        }

        // ==================================================================
        //  REJECT ORDER – GET
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> RejectOrder(int orderId)
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                         o.IsActive == Status.Active &&
                                         o.OrderStatus == OrderStatus.Ordered);
            if (order == null) return NotFound();

            return View(order);
        }

        // ==================================================================
        //  REJECT ORDER – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectOrder(int orderId, string reason)
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Please provide a reason for rejection.";
                return RedirectToAction(nameof(RejectOrder), new { orderId });
            }

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                         o.IsActive == Status.Active &&
                                         o.OrderStatus == OrderStatus.Ordered);
            if (order == null) return NotFound();

            order.OrderStatus = OrderStatus.Rejected;
            order.RejectedAt = DateTime.Now;
            order.RejectionReason = reason;
            await _context.SaveChangesAsync();

            if (order.CreatedByEmployeeId.HasValue)
            {
                try
                {
                    string item = order.Consumable?.Name ?? "Unknown item";
                    string msg = $"Your order for '{item}' (Qty: {order.QuantityRequested}) was rejected. Reason: {reason}";
                    await _notifService.NotifyUserAsync(
                        order.CreatedByEmployeeId.Value,
                        "Employee",
                        msg,
                        Url.Action("Orders", "ConsumablesManager"));
                }
                catch (Exception ex) { Console.WriteLine("Notify error: " + ex.Message); }
            }

            TempData["SuccessMessage"] = "Order rejected. The Consumables Manager has been notified.";
            return RedirectToAction("Index");
        }

        // ==================================================================
        //  ORDER HISTORY ARCHIVE
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> OrderHistory(DateTime? startDate, DateTime? endDate, string? searchTerm)
        {
            int? supplierId = GetCurrentSupplierId();
            if (supplierId == null) return RedirectToAction("Login", "Account");

            var query = _context.ConsumableOrders
                .Include(o => o.Consumable)
                .Where(o => o.SupplierId == supplierId
                            && o.IsActive == Status.Active
                            && (o.OrderStatus == OrderStatus.Complete || o.OrderStatus == OrderStatus.Fulfilled));

            if (startDate.HasValue)
                query = query.Where(o => o.RequestDate >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(o => o.RequestDate <= endDate.Value.AddDays(1));

            if (!string.IsNullOrWhiteSpace(searchTerm))
                query = query.Where(o => o.Consumable.Name.Contains(searchTerm));

            var orders = await query
                .OrderByDescending(o => o.RequestDate)
                .ToListAsync();

            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.SearchTerm = searchTerm;

            return View(orders);
        }
    }
}