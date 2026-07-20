using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    [Authorize(Roles = "CONSUMABLESMANAGER")]
    // [Route("[controller]")] ← REMOVED
    public class ConsumablesManagerController : Controller
    {
        private readonly WardDbContext _context;

        public ConsumablesManagerController(WardDbContext context)
        {
            _context = context;
        }

        private int? GetCurrentManagerId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(claim) || !int.TryParse(claim, out int id))
                return null;
            var role = User.FindFirstValue(ClaimTypes.Role);
            if (role != UserRole.CONSUMABLESMANAGER.ToString())
                return null;
            return id;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            // Total consumables (active)
            ViewBag.ActiveConsumablesCount = await _context.Consumables
                .CountAsync(c => c.IsActive == Status.Active);

            // Total stock on hand (sum of all active consumables)
            ViewBag.TotalStockOnHand = await _context.Consumables
                .Where(c => c.IsActive == Status.Active)
                .SumAsync(c => c.QuantityOnHand);

            // Low stock items (QuantityOnHand <= ReorderLevel)
            ViewBag.LowStockCount = await _context.Consumables
                .Where(c => c.IsActive == Status.Active && c.QuantityOnHand <= c.ReorderLevel)
                .CountAsync();

            // Pending orders (status = Ordered and active)
            ViewBag.PendingOrdersCount = await _context.ConsumableOrders
                .CountAsync(o => o.OrderStatus == OrderStatus.Ordered && o.IsActive == Status.Active);

            // Orders delivered today
            var today = DateTime.Today;
            ViewBag.OrdersDeliveredToday = await _context.ConsumableOrders
                .Where(o => o.OrderStatus == OrderStatus.Complete
                            && o.IsActive == Status.Active
                            && o.ReceivedDate.HasValue
                            && o.ReceivedDate.Value.Date == today)
                .CountAsync();

            // Weekly stock take due / last stock take info
            var lastStockTake = await _context.StockTakes
                .Where(s => s.IsActive == Status.Active)
                .OrderByDescending(s => s.DateTaken)
                .FirstOrDefaultAsync();

            if (lastStockTake == null)
            {
                ViewBag.StockTakeDue = "Due (no stock take recorded)";
            }
            else
            {
                var daysSinceLast = (DateTime.Today - lastStockTake.DateTaken.Date).Days;
                ViewBag.LastStockTakeDate = lastStockTake.DateTaken.ToString("dd MMM yyyy");
                if (daysSinceLast >= 7)
                    ViewBag.StockTakeDue = $"Due (last was {ViewBag.LastStockTakeDate})";
                else
                    ViewBag.StockTakeDue = $"Next due after {ViewBag.LastStockTakeDate}";
            }

            return View();
        }

        // ==================================================================
        //  CONSUMABLES – CRUD + SOFT DELETE
        // ==================================================================

        // LIST
        [HttpGet]
        public async Task<IActionResult> Consumables(string status = "Active")
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Consumables.AsQueryable();
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
                query = query.Where(c => c.IsActive == parsedStatus);

            var consumables = await query.OrderBy(c => c.Name).ToListAsync();

            var statusOptions = new List<SelectListItem>
            {
                new SelectListItem("Active", "Active", status == "Active"),
                new SelectListItem("Inactive", "Inactive", status == "Inactive"),
                new SelectListItem("All", "All", status == "All")
            };
            ViewBag.Statuses = new SelectList(statusOptions, "Value", "Text", status);

            return View(consumables);
        }

        // CREATE – GET
        [HttpGet]
        public IActionResult CreateConsumable()
        {
            return View();
        }

        // CREATE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateConsumable(Consumable consumable)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            if (!ModelState.IsValid) return View(consumable);

            consumable.IsActive = Status.Active;
            _context.Consumables.Add(consumable);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable created.";
            return RedirectToAction(nameof(Consumables));
        }

        // EDIT – GET
        [HttpGet]
        public async Task<IActionResult> EditConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        // EDIT – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditConsumable(int id, Consumable posted)
        {
            if (id != posted.Id) return BadRequest();
            if (!ModelState.IsValid) return View(posted);

            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();

            consumable.Name = posted.Name;
            consumable.Description = posted.Description;
            consumable.QuantityOnHand = posted.QuantityOnHand;
            consumable.ReorderLevel = posted.ReorderLevel;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable updated.";
            return RedirectToAction(nameof(Consumables));
        }

        // DETAILS
        [HttpGet]
        public async Task<IActionResult> DetailsConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        // SOFT DELETE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();

            consumable.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable deactivated.";
            return RedirectToAction(nameof(Consumables));
        }

        // RESTORE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();

            consumable.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable reactivated.";
            return RedirectToAction(nameof(Consumables));
        }

        // ==================================================================
        //  REQUEST CONSUMABLES (Order to store)
        // ==================================================================

        // LIST ORDERS
        [HttpGet]
        public async Task<IActionResult> Orders()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var orders = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .Where(o => o.CreatedByEmployeeId == managerId)
                .OrderByDescending(o => o.RequestDate)
                .ToListAsync();
            return View(orders);
        }

        // CREATE – GET
        // REPLACE the existing RequestConsumable (GET) method with this version
        [HttpGet]
        public IActionResult RequestConsumable(int? consumableId, int? quantity)
        {
            ViewBag.Consumables = new SelectList(
                _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name),
                "Id", "Name", consumableId);

            // Pre‑fill quantity if provided (e.g. from LowStockAlerts)
            var model = new ConsumableOrder
            {
                ConsumableId = consumableId ?? 0,
                QuantityRequested = quantity ?? 0
            };

            return View(model);
        }

        // CREATE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestConsumable(ConsumableOrder order)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("Consumable");
            if (!ModelState.IsValid)
            {
                ViewBag.Consumables = new SelectList(
                    _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name),
                    "Id", "Name", order.ConsumableId);
                return View(order);
            }

            order.OrderStatus = OrderStatus.Ordered;
            order.IsUrgent = order.IsUrgent; // retain the value from the form
            order.RequestDate = DateTime.Now;
            order.IsActive = Status.Active;
            order.CreatedByEmployeeId = managerId;
            _context.ConsumableOrders.Add(order);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Order placed.";
            return RedirectToAction(nameof(Orders));
        }

        // DETAILS
        [HttpGet]
        public async Task<IActionResult> OrderDetails(int id)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == id && o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();
            return View(order);
        }

        // EDIT – GET
        [HttpGet]
        public async Task<IActionResult> EditOrder(int id)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == id && o.IsActive == Status.Active && o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();

            if (order.OrderStatus != OrderStatus.Ordered)
            {
                TempData["ErrorMessage"] = "Only 'Ordered' requests can be edited.";
                return RedirectToAction(nameof(Orders));
            }

            ViewBag.Consumables = new SelectList(
                _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name),
                "Id", "Name", order.ConsumableId);
            return View(order);
        }

        // EDIT – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditOrder(int id, ConsumableOrder posted)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            ModelState.Remove("Consumable"); ModelState.Remove("IsActive"); ModelState.Remove("RequestDate");
            ModelState.Remove("OrderStatus"); ModelState.Remove("ReceivedDate"); ModelState.Remove("QuantityReceived");

            if (!ModelState.IsValid)
            {
                ViewBag.Consumables = new SelectList(
                    _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name),
                    "Id", "Name", posted.ConsumableId);
                return View(posted);
            }

            var order = await _context.ConsumableOrders
                .FirstOrDefaultAsync(o => o.Id == id && o.IsActive == Status.Active && o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();

            if (order.OrderStatus != OrderStatus.Ordered)
            {
                TempData["ErrorMessage"] = "Only 'Ordered' requests can be edited.";
                return RedirectToAction(nameof(Orders));
            }

            order.ConsumableId = posted.ConsumableId;
            order.QuantityRequested = posted.QuantityRequested;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Order updated.";
            return RedirectToAction(nameof(Orders));
        }

        // SOFT DELETE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .FirstOrDefaultAsync(o => o.Id == id && o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();

            order.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Order cancelled (soft deleted).";
            return RedirectToAction(nameof(Orders));
        }

        // RESTORE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreOrder(int id)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .FirstOrDefaultAsync(o => o.Id == id && o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();

            order.IsActive = Status.Active;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Order restored.";
            return RedirectToAction(nameof(Orders));
        }

        // ==================================================================
        //  RECEIVE CONSUMABLES (Update stock on delivery)
        // ==================================================================

        // GET – show order and allow entering received quantity
        // GET – show order and allow entering received quantity
        [HttpGet]
        public async Task<IActionResult> ReceiveConsumable(int orderId)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId
                                         && (o.OrderStatus == OrderStatus.Ordered || o.OrderStatus == OrderStatus.PartiallyFulfilled)
                                         && o.IsActive == Status.Active
                                         && o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();

            return View(order);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveConsumable(int orderId, int quantityReceived, string? notes)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .Include(o => o.Supplier) // include supplier for display
                .FirstOrDefaultAsync(o => o.Id == orderId
                                         && (o.OrderStatus == OrderStatus.Ordered || o.OrderStatus == OrderStatus.PartiallyFulfilled)
                                         && o.IsActive == Status.Active
                                         && o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();

            if (quantityReceived <= 0)
            {
                TempData["ErrorMessage"] = "Quantity must be greater than zero.";
                return RedirectToAction(nameof(ReceiveConsumable), new { orderId });
            }

            int previousReceived = order.QuantityReceived ?? 0;
            int totalReceived = previousReceived + quantityReceived;

            // Update stock
            order.Consumable.QuantityOnHand += quantityReceived;
            order.ReceivedDate = DateTime.Now;
            order.QuantityReceived = totalReceived;

            // Determine status
            if (totalReceived >= order.QuantityRequested)
            {
                order.OrderStatus = OrderStatus.Complete;
                order.MissingQuantity = 0;
            }
            else
            {
                order.OrderStatus = OrderStatus.PartiallyFulfilled;
                order.MissingQuantity = order.QuantityRequested - totalReceived;
            }

            // Append delivery notes
            if (!string.IsNullOrWhiteSpace(notes))
            {
                string managerName = (await _context.Employees.FindAsync(managerId))?.FullName ?? "Unknown";
                string deliveryNote = $"Received by {managerName} on {DateTime.Now:dd MMM yyyy HH:mm}: {notes}";
                order.Notes = string.IsNullOrEmpty(order.Notes) ? deliveryNote : $"{order.Notes} | {deliveryNote}";
            }

            // Record stock movement
            _context.StockMovements.Add(new StockMovement
            {
                ConsumableId = order.ConsumableId,
                QuantityChange = quantityReceived,
                MovementType = MovementType.Received,
                Reason = $"Received from order #{order.Id}",
                MovementDate = DateTime.Now,
                ConsumableOrderId = order.Id
            });

            await _context.SaveChangesAsync();

            int outstanding = order.QuantityRequested - (order.QuantityReceived ?? 0);
            string msg = $"Received {quantityReceived} units. Total received: {order.QuantityReceived} of {order.QuantityRequested}.";
            if (outstanding > 0) msg += $" Outstanding: {outstanding}.";

            TempData["SuccessMessage"] = msg;
            return RedirectToAction(nameof(Orders));
        }



        // ==================================================================
        //  TAKE STOCK (Weekly physical count)
        // ==================================================================

        // LIST stock takes
        [HttpGet]
        public async Task<IActionResult> StockTakes()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var stockTakes = await _context.StockTakes
                .Include(s => s.StockTakeItems)
                .Where(s => s.CreatedByEmployeeId == managerId)
                .OrderByDescending(s => s.DateTaken)
                .ToListAsync();
            return View(stockTakes);
        }

        // CREATE STOCK TAKE – GET
        [HttpGet]
        public IActionResult TakeStock()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");
            return View(new StockTake { DateTaken = DateTime.Now });
        }

        // CREATE STOCK TAKE – POST (header)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartStockTake(StockTake stockTake)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            ModelState.Remove("Id");
            ModelState.Remove("StockTakeItems");
            if (!ModelState.IsValid) return View("TakeStock", stockTake);

            stockTake.IsActive = Status.Active;
            stockTake.CreatedByEmployeeId = managerId;
            _context.StockTakes.Add(stockTake);
            await _context.SaveChangesAsync();

            return RedirectToAction("CountStock", new { stockTakeId = stockTake.Id });
        }

        // EDIT STOCK TAKE HEADER – GET
        [HttpGet]
        public async Task<IActionResult> EditStockTake(int id)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var stockTake = await _context.StockTakes
                .FirstOrDefaultAsync(s => s.Id == id && s.IsActive == Status.Active
                                         && s.CreatedByEmployeeId == managerId);
            if (stockTake == null) return NotFound();
            return View(stockTake);
        }

        // EDIT STOCK TAKE HEADER – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditStockTake(int id, StockTake posted)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            if (id != posted.Id) return BadRequest();
            ModelState.Remove("StockTakeItems");
            if (!ModelState.IsValid) return View(posted);

            var stockTake = await _context.StockTakes
                .FirstOrDefaultAsync(s => s.Id == id && s.IsActive == Status.Active
                                         && s.CreatedByEmployeeId == managerId);
            if (stockTake == null) return NotFound();

            stockTake.DateTaken = posted.DateTaken;
            stockTake.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Stock take header updated.";
            return RedirectToAction(nameof(StockTakes));
        }

        // COUNT STOCK – GET (shows all active consumables with current quantities)
        [HttpGet]
        public async Task<IActionResult> CountStock(int stockTakeId)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var stockTake = await _context.StockTakes
                .Include(s => s.StockTakeItems)
                .FirstOrDefaultAsync(s => s.Id == stockTakeId && s.IsActive == Status.Active
                                         && s.CreatedByEmployeeId == managerId);
            if (stockTake == null) return NotFound();

            var consumables = await _context.Consumables
                .Where(c => c.IsActive == Status.Active)
                .OrderBy(c => c.Name)
                .ToListAsync();

            ViewBag.StockTakeId = stockTakeId;
            ViewBag.StockTakeDate = stockTake.DateTaken;
            return View(consumables);
        }

        // SAVE STOCK COUNT – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveStockCount(int stockTakeId, Dictionary<int, int> actualQuantities)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var stockTake = await _context.StockTakes
                .FirstOrDefaultAsync(s => s.Id == stockTakeId && s.IsActive == Status.Active
                                         && s.CreatedByEmployeeId == managerId);
            if (stockTake == null) return NotFound();

            foreach (var entry in actualQuantities)
            {
                int consumableId = entry.Key;
                int actualQty = entry.Value;

                var consumable = await _context.Consumables.FindAsync(consumableId);
                if (consumable == null) continue;

                _context.StockTakeItems.Add(new StockTakeItem
                {
                    StockTakeId = stockTakeId,
                    ConsumableId = consumableId,
                    SystemQuantity = consumable.QuantityOnHand,
                    ActualQuantity = actualQty
                });

                // Optionally auto‑update system quantity (commented out by default)
                // consumable.QuantityOnHand = actualQty;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Stock count saved.";
            return RedirectToAction(nameof(StockTakes));
        }

        // DETAILS
        [HttpGet]
        public async Task<IActionResult> StockTakeDetails(int id)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var stockTake = await _context.StockTakes
                .Include(s => s.StockTakeItems).ThenInclude(si => si.Consumable)
                .FirstOrDefaultAsync(s => s.Id == id && s.CreatedByEmployeeId == managerId);
            if (stockTake == null) return NotFound();
            return View(stockTake);
        }

        // SOFT DELETE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStockTake(int id)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var stockTake = await _context.StockTakes
                .FirstOrDefaultAsync(s => s.Id == id && s.CreatedByEmployeeId == managerId);
            if (stockTake == null) return NotFound();

            stockTake.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Stock take deactivated.";
            return RedirectToAction(nameof(StockTakes));
        }

        // RESTORE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreStockTake(int id)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var stockTake = await _context.StockTakes
                .FirstOrDefaultAsync(s => s.Id == id && s.CreatedByEmployeeId == managerId);
            if (stockTake == null) return NotFound();

            stockTake.IsActive = Status.Active;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Stock take restored.";
            return RedirectToAction(nameof(StockTakes));
        }

        // ==================================================================
        //  LIST RECEIVED ORDERS
        // ==================================================================

        [HttpGet]
        public async Task<IActionResult> ReceivedOrders()
        {
            var received = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .Where(o => o.OrderStatus == OrderStatus.Complete && o.IsActive == Status.Active)
                .OrderByDescending(o => o.ReceivedDate)
                .ToListAsync();
            return View(received);
        }

        // ==================================================================
        //  SOFT DELETE RECEIVED ORDER – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteReceivedOrder(int id)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == id && o.OrderStatus == OrderStatus.Complete && o.IsActive == Status.Active);
            if (order == null) return NotFound();

            // Reverse the stock that was added when the order was received
            if (order.QuantityReceived > 0)
                order.Consumable.QuantityOnHand -= order.QuantityReceived.Value;

            order.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Received order deactivated; stock has been reversed.";
            return RedirectToAction(nameof(ReceivedOrders));
        }

        // ==================================================================
        //  RESTORE RECEIVED ORDER – POST
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreReceivedOrder(int id)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == id && o.OrderStatus == OrderStatus.Complete && o.IsActive != Status.Active);
            if (order == null) return NotFound();

            if (order.QuantityReceived > 0)
                order.Consumable.QuantityOnHand += order.QuantityReceived.Value;

            order.IsActive = Status.Active;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Received order restored; stock has been re‑added.";
            return RedirectToAction(nameof(ReceivedOrders));
        }

        // ==================================================================
        //  LOW STOCK ALERTS – items below reorder level
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> LowStockAlerts()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var lowStockItems = await _context.Consumables
                .Where(c => c.IsActive == Status.Active && c.QuantityOnHand <= c.ReorderLevel)
                .OrderBy(c => c.Name)
                .ToListAsync();

            return View(lowStockItems);
        }


        // ==================================================================
        //  PENDING REQUESTS – only "Ordered" orders
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PendingRequests()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var pending = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .Where(o => o.OrderStatus == OrderStatus.Ordered
                            && o.IsActive == Status.Active
                            && o.CreatedByEmployeeId == managerId)
                .OrderByDescending(o => o.RequestDate)
                .ToListAsync();

            return View(pending);
        }

        // ==================================================================
        //  CANCEL ORDER (soft delete with reason)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelOrder(int id, string cancelReason)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .FirstOrDefaultAsync(o => o.Id == id
                                          && o.OrderStatus == OrderStatus.Ordered
                                          && o.IsActive == Status.Active
                                          && o.CreatedByEmployeeId == managerId);
            if (order == null)
            {
                TempData["ErrorMessage"] = "Request not found or cannot be cancelled.";
                return RedirectToAction(nameof(PendingRequests));
            }

            // Record cancellation reason (append to existing reason if any)
            string note = string.IsNullOrWhiteSpace(cancelReason) ? "Cancelled" : $"Cancelled: {cancelReason}";
            order.Reason = string.IsNullOrEmpty(order.Reason) ? note : $"{order.Reason} | {note}";

            order.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Request cancelled.";
            return RedirectToAction(nameof(PendingRequests));
        }



        // ==================================================================
        //  VARIANCE REPORT – shortages / overages from a stock take
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> VarianceReport(int? stockTakeId)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            StockTake? stockTake = null;

            if (stockTakeId.HasValue)
            {
                stockTake = await _context.StockTakes
                    .FirstOrDefaultAsync(s => s.Id == stockTakeId.Value
                                             && s.IsActive == Status.Active
                                             && s.CreatedByEmployeeId == managerId);
            }
            else
            {
                // Default to the latest active stock take for the manager
                stockTake = await _context.StockTakes
                    .Where(s => s.IsActive == Status.Active && s.CreatedByEmployeeId == managerId)
                    .OrderByDescending(s => s.DateTaken)
                    .FirstOrDefaultAsync();
            }

            if (stockTake == null)
            {
                TempData["ErrorMessage"] = "No stock take found. Please perform a stock take first.";
                return RedirectToAction(nameof(StockTakes));
            }

            // Load items with consumable details
            var items = await _context.StockTakeItems
                .Include(si => si.Consumable)
                .Where(si => si.StockTakeId == stockTake.Id)
                .OrderBy(si => si.Consumable.Name)
                .ToListAsync();

            ViewBag.StockTakeId = stockTake.Id;
            ViewBag.StockTakeDate = stockTake.DateTaken;

            // Dropdown for selecting other stock takes
            ViewBag.StockTakes = new SelectList(
                await _context.StockTakes
                    .Where(s => s.IsActive == Status.Active && s.CreatedByEmployeeId == managerId)
                    .OrderByDescending(s => s.DateTaken)
                    .ToListAsync(),
                "Id", "DateTaken", stockTake.Id);

            return View(items);
        }


        // ==================================================================
        //  ADJUST STOCK – manual inventory correction
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> AdjustStock(int? consumableId)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            // Dropdown of active consumables
            ViewBag.Consumables = new SelectList(
                await _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name).ToListAsync(),
                "Id", "Name", consumableId);

            // Pre-select if coming from a specific consumable
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustStock(int consumableId, int quantityChange, string reason)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumable = await _context.Consumables.FindAsync(consumableId);
            if (consumable == null || consumable.IsActive != Status.Active)
            {
                TempData["ErrorMessage"] = "Invalid consumable.";
                return RedirectToAction(nameof(AdjustStock));
            }

            if (quantityChange == 0)
            {
                TempData["ErrorMessage"] = "Quantity change cannot be zero.";
                return RedirectToAction(nameof(AdjustStock));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Please provide a reason.";
                return RedirectToAction(nameof(AdjustStock));
            }

            // Update stock
            consumable.QuantityOnHand += quantityChange;
            if (consumable.QuantityOnHand < 0) consumable.QuantityOnHand = 0;

            // Record movement
            _context.StockMovements.Add(new StockMovement
            {
                ConsumableId = consumableId,
                QuantityChange = quantityChange,
                MovementType = MovementType.Adjustment,
                Reason = reason,
                MovementDate = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Stock adjusted by {quantityChange}. New quantity: {consumable.QuantityOnHand}.";
            return RedirectToAction(nameof(Consumables));
        }


        // ==================================================================
        //  RECORD USAGE (issue items from stock)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> RecordUsage(int? consumableId)
        {
            ViewBag.Consumables = new SelectList(
                await _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name).ToListAsync(),
                "Id", "Name", consumableId);

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordUsage(int consumableId, int quantityUsed, string? notes)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumable = await _context.Consumables.FindAsync(consumableId);
            if (consumable == null || consumable.IsActive != Status.Active)
            {
                TempData["ErrorMessage"] = "Invalid consumable.";
                return RedirectToAction(nameof(RecordUsage));
            }

            if (quantityUsed <= 0)
            {
                TempData["ErrorMessage"] = "Quantity must be positive.";
                return RedirectToAction(nameof(RecordUsage));
            }

            if (quantityUsed > consumable.QuantityOnHand)
            {
                TempData["ErrorMessage"] = "Not enough stock on hand.";
                return RedirectToAction(nameof(RecordUsage));
            }

            // Deduct stock
            consumable.QuantityOnHand -= quantityUsed;

            // Record movement
            _context.StockMovements.Add(new StockMovement
            {
                ConsumableId = consumableId,
                QuantityChange = -quantityUsed,   // negative for issue
                MovementType = MovementType.Issued,
                Reason = string.IsNullOrWhiteSpace(notes) ? "Issued for use" : notes,
                MovementDate = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{quantityUsed} {consumable.Name} issued. Remaining: {consumable.QuantityOnHand}.";
            return RedirectToAction(nameof(ConsumptionHistory));
        }



        // ==================================================================
        //  CONSUMPTION HISTORY – daily usage summary
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> ConsumptionHistory(int? consumableId, DateTime? fromDate, DateTime? toDate)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var movements = _context.StockMovements
                .Include(m => m.Consumable)
                .Where(m => m.MovementType == MovementType.Issued)
                .AsQueryable();

            if (consumableId.HasValue && consumableId > 0)
                movements = movements.Where(m => m.ConsumableId == consumableId.Value);

            if (fromDate.HasValue)
                movements = movements.Where(m => m.MovementDate >= fromDate.Value);
            if (toDate.HasValue)
                movements = movements.Where(m => m.MovementDate <= toDate.Value.Date.AddDays(1));

            var list = await movements
                .OrderByDescending(m => m.MovementDate)
                .ToListAsync();

            // Daily summary (for display)
            var dailySummary = list
                .GroupBy(m => new { m.MovementDate.Date, m.Consumable?.Name })
                .Select(g => new
                {
                    Date = g.Key.Date,
                    Consumable = g.Key.Name,
                    TotalUsed = g.Sum(m => -m.QuantityChange)   // quantityChange is negative
                })
                .OrderByDescending(d => d.Date)
                .ToList();

            ViewBag.Consumables = new SelectList(
                await _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name).ToListAsync(),
                "Id", "Name", consumableId);
            ViewBag.DailySummary = dailySummary;

            return View(list);   // pass the raw movements for detail
        }


        // ==================================================================
        //  STOCK REPORT – printable stock overview
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> StockReport()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumables = await _context.Consumables
                .Where(c => c.IsActive == Status.Active)
                .OrderBy(c => c.Name)
                .ToListAsync();

            return View(consumables);
        }


        // ==================================================================
        //  WEEKLY CONSUMPTION REPORT – total used this week
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> WeeklyConsumptionReport(DateTime? weekStart)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            // Default to this Monday
            var today = DateTime.Today;
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var startOfWeek = weekStart ?? today.AddDays(-diff).Date;
            var endOfWeek = startOfWeek.AddDays(7);

            var usage = await _context.StockMovements
                .Include(m => m.Consumable)
                .Where(m => m.MovementType == MovementType.Issued
                            && m.MovementDate >= startOfWeek
                            && m.MovementDate < endOfWeek)
                .GroupBy(m => new { m.ConsumableId, m.Consumable.Name })
                .Select(g => new
                {
                    ConsumableName = g.Key.Name,
                    TotalUsed = g.Sum(m => -m.QuantityChange)   // quantityChange is negative
                })
                .OrderBy(x => x.ConsumableName)
                .ToListAsync();

            ViewBag.WeekStart = startOfWeek;
            ViewBag.WeekEnd = endOfWeek.AddDays(-1); // last day

            return View(usage);
        }


        // ==================================================================
        //  MONTHLY USAGE REPORT – usage trends per month
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> MonthlyUsageReport(int? consumableId, int? year)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            int selectedYear = year ?? DateTime.Today.Year;

            // Base query: issued movements for the selected year
            var issued = _context.StockMovements
                .Include(m => m.Consumable)
                .Where(m => m.MovementType == MovementType.Issued
                            && m.MovementDate.Year == selectedYear);

            // Filter by consumable if selected
            if (consumableId.HasValue && consumableId > 0)
                issued = issued.Where(m => m.ConsumableId == consumableId.Value);

            // Group by month and consumable, then aggregate
            var monthlyData = await issued
                .GroupBy(m => new { m.MovementDate.Month, m.Consumable.Name })
                .Select(g => new
                {
                    Month = g.Key.Month,
                    ConsumableName = g.Key.Name,
                    TotalUsed = g.Sum(m => -m.QuantityChange)   // negative -> positive
                })
                .ToListAsync();

            // Build chart data for all months (1..12) per consumable
            var labels = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                         "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

            var datasets = monthlyData
                .GroupBy(d => d.ConsumableName)
                .Select(g => new
                {
                    Label = g.Key ?? "All",
                    Data = Enumerable.Range(1, 12).Select(m => g.Where(x => x.Month == m).Sum(x => x.TotalUsed)).ToList()
                })
                .ToList();

            // Pass chart data to the view
            ViewBag.ChartLabels = labels;
            ViewBag.ChartDatasets = datasets;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.ConsumableId = consumableId;

            // Dropdowns
            ViewBag.Consumables = new SelectList(
                await _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name).ToListAsync(),
                "Id", "Name", consumableId);

            return View();
        }


        // ==================================================================
        //  PRINTABLE STOCK TAKE SHEET – for manual counting
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PrintStockTakeSheet()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var consumables = await _context.Consumables
                .Where(c => c.IsActive == Status.Active)
                .OrderBy(c => c.Name)
                .ToListAsync();

            return View(consumables);
        }


        // ==================================================================
        //  SCAN QR CODE – opens camera to scan
        // ==================================================================
        [HttpGet]
        public IActionResult ScanQR()
        {
            return View();
        }

        // ==================================================================
        //  PRINT QR CODES – printable sheet with QR codes for all active items
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> PrintQRCodes()
        {
            var consumables = await _context.Consumables
                .Where(c => c.IsActive == Status.Active)
                .OrderBy(c => c.Name)
                .ToListAsync();

            return View(consumables);
        }


    }
}