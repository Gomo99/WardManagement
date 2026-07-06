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
    [Route("[controller]")]
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


            ViewBag.ActiveConsumablesCount = await _context.Consumables.CountAsync(c => c.IsActive == Status.Active);
            ViewBag.PendingOrdersCount = await _context.ConsumableOrders.CountAsync(o => o.OrderStatus == OrderStatus.Ordered && o.IsActive == Status.Active);
            ViewBag.StockTakesCount = await _context.StockTakes.CountAsync(s => s.IsActive == Status.Active);
            return View();
        }

        // ==================================================================
        //  CONSUMABLES – CRUD + SOFT DELETE
        // ==================================================================

        // LIST
        [HttpGet("Consumables")]
        public async Task<IActionResult> Consumables(string status = "Active")
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var query = _context.Consumables.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<Status>(status, out var parsedStatus))
            {
                query = query.Where(c => c.IsActive == parsedStatus);
            }
            // If status is "All" or invalid, no filter → shows all consumables.

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
        [HttpGet("CreateConsumable")]
        public IActionResult CreateConsumable()
        {
            return View();
        }

        // CREATE – POST
        [HttpPost("CreateConsumable")]
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
        [HttpGet("EditConsumable")]
        public async Task<IActionResult> EditConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        // EDIT – POST
        [HttpPost("EditConsumable")]
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
        [HttpGet("DetailsConsumable/{id:int}")]
        public async Task<IActionResult> DetailsConsumable(int id)
        {
            var consumable = await _context.Consumables.FindAsync(id);
            if (consumable == null) return NotFound();
            return View(consumable);
        }

        // SOFT DELETE
        [HttpPost("DeleteConsumable/{id:int}")]
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
        [HttpGet("Orders")]

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
        [HttpGet("RequestConsumable")]
        public IActionResult RequestConsumable()
        {
            ViewBag.Consumables = new SelectList(
                _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name),
                "Id", "Name");
            return View();
        }

        // CREATE – POST
        [HttpPost("RequestConsumable")]
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
            order.CreatedByEmployeeId = managerId;   // ownership
            _context.ConsumableOrders.Add(order);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Order placed.";
            return RedirectToAction(nameof(Orders));
        }

        // DETAILS
        [HttpGet("OrderDetails/{id:int}")]
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
        [HttpGet("EditOrder")]
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
        [HttpPost("EditOrder")]
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
        [HttpPost("DeleteOrder/{id:int}")]
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
        [HttpPost("RestoreOrder")]
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
        [HttpGet("ReceiveConsumable")]
        public async Task<IActionResult> ReceiveConsumable(int orderId)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                         o.OrderStatus == OrderStatus.Fulfilled &&   // <-- changed
                                         o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();
            return View(order);
        }

        [HttpPost("ReceiveConsumable")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveConsumable(int orderId, int quantityReceived)
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");

            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId &&
                                         o.OrderStatus == OrderStatus.Fulfilled &&   // <-- changed
                                         o.CreatedByEmployeeId == managerId);
            if (order == null) return NotFound();

            if (quantityReceived <= 0)
            {
                TempData["ErrorMessage"] = "Quantity must be greater than zero.";
                return RedirectToAction(nameof(ReceiveConsumable), new { orderId });
            }

            order.Consumable.QuantityOnHand += quantityReceived;
            order.OrderStatus = OrderStatus.Complete;
            order.ReceivedDate = DateTime.Now;
            order.QuantityReceived = quantityReceived;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Received {quantityReceived} units. Stock updated.";
            return RedirectToAction(nameof(Orders));
        }

        // ==================================================================
        //  TAKE STOCK (Weekly physical count)
        // ==================================================================

        // LIST stock takes
        [HttpGet("StockTakes")]
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
        [HttpGet("TakeStock")]
        public IActionResult TakeStock()
        {
            int? managerId = GetCurrentManagerId();
            if (managerId == null) return RedirectToAction("Login", "Account");
            return View(new StockTake { DateTaken = DateTime.Now });
        }

        // CREATE STOCK TAKE – POST (header)
        [HttpPost("TakeStock")]
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
        [HttpGet("EditStockTake")]
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
        [HttpPost("EditStockTake")]
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
        [HttpGet("CountStock")]
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
        [HttpPost("SaveStockCount")]
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
        [HttpGet("StockTakeDetails/ {id:int}")]
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
        [HttpPost("DeleteStockTake/{id:int}")]
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
        [HttpPost("RestoreStockTake/{id:int}")]
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
        //  LIST RECEIVED ORDERS (optional – you can reuse Orders with a filter)
        // ==================================================================

        [HttpGet("ReceivedOrders")]
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
        //  (deactivates the order and reverses its stock addition)
        // ==================================================================
        [HttpPost("DeleteReceivedOrder/{id:int}")]
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
        //  (reactivates the order and re‑adds the stock)
        // ==================================================================
        [HttpPost("RestoreReceivedOrder/{int:id}")]
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

    }
}