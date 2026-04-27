using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WARDMANAGEMENTSYSTEM.AppStatus;
using WARDMANAGEMENTSYSTEM.Data;
using WARDMANAGEMENTSYSTEM.Models;

namespace WARDMANAGEMENTSYSTEM.Controllers
{
    public class ConsumablesManagerController : Controller
    {
        private readonly WardDbContext _context;

        public ConsumablesManagerController(WardDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------
        //  DASHBOARD
        // ------------------------------------------------------------------
        public async Task<IActionResult> Dashboard()
        {
            ViewBag.ActiveConsumablesCount = await _context.Consumables.CountAsync(c => c.IsActive == Status.Active);
            ViewBag.PendingOrdersCount = await _context.ConsumableOrders.CountAsync(o => o.OrderStatus == OrderStatus.Ordered && o.IsActive == Status.Active);
            ViewBag.StockTakesCount = await _context.StockTakes.CountAsync(s => s.IsActive == Status.Active);
            return View();
        }

        // ==================================================================
        //  CONSUMABLES – CRUD + SOFT DELETE
        // ==================================================================

        // LIST
        public async Task<IActionResult> Consumables()
        {
            var consumables = await _context.Consumables
                .OrderBy(c => c.Name)
                .ToListAsync();
            return View(consumables);
        }

        // CREATE – GET
        public IActionResult CreateConsumable()
        {
            return View();
        }

        // CREATE – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateConsumable(Consumable consumable)
        {
            ModelState.Remove("Id");
            if (!ModelState.IsValid) return View(consumable);

            consumable.IsActive = Status.Active;
            _context.Consumables.Add(consumable);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Consumable created.";
            return RedirectToAction(nameof(Consumables));
        }

        // EDIT – GET
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
        public async Task<IActionResult> Orders()
        {
            var orders = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .OrderByDescending(o => o.RequestDate)
                .ToListAsync();
            return View(orders);
        }

        // CREATE ORDER – GET
        public IActionResult RequestConsumable()
        {
            ViewBag.Consumables = new SelectList(
                _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name),
                "Id", "Name");
            return View();
        }

        // CREATE ORDER – POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestConsumable(ConsumableOrder order)
        {
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
            order.RequestDate = DateTime.Now;
            order.IsActive = Status.Active;
            _context.ConsumableOrders.Add(order);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Order placed.";
            return RedirectToAction(nameof(Orders));
        }

        // DETAILS – GET
        public async Task<IActionResult> OrderDetails(int id)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();
            return View(order);
        }


        [HttpGet]
        public async Task<IActionResult> EditOrder(int id)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == id && o.IsActive == Status.Active);

            if (order == null) return NotFound();

            // Only allow editing if the order hasn’t been completed (optional, but logical)
            if (order.OrderStatus != OrderStatus.Ordered)
            {
                TempData["ErrorMessage"] = "Only orders that are still 'Ordered' can be edited.";
                return RedirectToAction(nameof(Orders));
            }

            ViewBag.Consumables = new SelectList(
                _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name),
                "Id", "Name", order.ConsumableId);
            return View(order);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditOrder(int id, ConsumableOrder posted)
        {
            if (id != posted.Id) return BadRequest();

            ModelState.Remove("Consumable");
            ModelState.Remove("IsActive");
            ModelState.Remove("RequestDate");
            ModelState.Remove("OrderStatus");
            ModelState.Remove("ReceivedDate");
            ModelState.Remove("QuantityReceived");
            ModelState.Remove("Notes");  // keep if you want to edit notes

            if (!ModelState.IsValid)
            {
                ViewBag.Consumables = new SelectList(
                    _context.Consumables.Where(c => c.IsActive == Status.Active).OrderBy(c => c.Name),
                    "Id", "Name", posted.ConsumableId);
                return View(posted);
            }

            var order = await _context.ConsumableOrders.FindAsync(id);
            if (order == null || order.IsActive != Status.Active) return NotFound();

            if (order.OrderStatus != OrderStatus.Ordered)
            {
                TempData["ErrorMessage"] = "Only orders still 'Ordered' can be edited.";
                return RedirectToAction(nameof(Orders));
            }

            order.ConsumableId = posted.ConsumableId;
            order.QuantityRequested = posted.QuantityRequested;
            // optionally: order.Notes = posted.Notes;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Order updated.";
            return RedirectToAction(nameof(Orders));
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            var order = await _context.ConsumableOrders.FindAsync(id);
            if (order == null) return NotFound();

            order.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Order cancelled (soft deleted).";
            return RedirectToAction(nameof(Orders));
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreOrder(int id)
        {
            var order = await _context.ConsumableOrders.FindAsync(id);
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
        [HttpGet]
        public async Task<IActionResult> ReceiveConsumable(int orderId)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.OrderStatus == OrderStatus.Ordered);

            if (order == null) return NotFound();

            return View(order);
        }

        // POST – confirm receipt
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveConsumable(int orderId, int quantityReceived)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.OrderStatus == OrderStatus.Ordered);

            if (order == null) return NotFound();

            if (quantityReceived <= 0)
            {
                TempData["ErrorMessage"] = "Quantity must be greater than zero.";
                return RedirectToAction(nameof(ReceiveConsumable), new { orderId });
            }

            // Update stock on hand
            order.Consumable.QuantityOnHand += quantityReceived;

            // Mark order as received
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
        public async Task<IActionResult> StockTakes()
        {
            var stockTakes = await _context.StockTakes
                .Include(s => s.StockTakeItems)
                .OrderByDescending(s => s.DateTaken)
                .ToListAsync();
            return View(stockTakes);
        }

        // NEW STOCK TAKE – GET
        [HttpGet]
        public IActionResult TakeStock()
        {
            // Create a new StockTake with today's date
            return View(new StockTake { DateTaken = DateTime.Now });
        }

        // NEW STOCK TAKE – POST (creates the header and redirects to count items)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartStockTake(StockTake stockTake)
        {
            ModelState.Remove("Id");
            ModelState.Remove("StockTakeItems");
            if (!ModelState.IsValid) return View("TakeStock", stockTake);

            stockTake.IsActive = Status.Active;
            _context.StockTakes.Add(stockTake);
            await _context.SaveChangesAsync();

            // Now redirect to the counting page
            return RedirectToAction("CountStock", new { stockTakeId = stockTake.Id });
        }

        // COUNT STOCK – GET (shows all consumables with current system quantity)
        [HttpGet]
        public async Task<IActionResult> CountStock(int stockTakeId)
        {
            var stockTake = await _context.StockTakes
                .Include(s => s.StockTakeItems)
                .FirstOrDefaultAsync(s => s.Id == stockTakeId && s.IsActive == Status.Active);
            if (stockTake == null) return NotFound();

            var consumables = await _context.Consumables
                .Where(c => c.IsActive == Status.Active)
                .OrderBy(c => c.Name)
                .ToListAsync();

            ViewBag.StockTakeId = stockTakeId;
            ViewBag.StockTakeDate = stockTake.DateTaken;

            // For each consumable, prepare a form to enter the actual quantity
            return View(consumables);
        }

        // COUNT STOCK – POST (save the counted items)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveStockCount(int stockTakeId, Dictionary<int, int> actualQuantities)
        {
            var stockTake = await _context.StockTakes
                .FirstOrDefaultAsync(s => s.Id == stockTakeId && s.IsActive == Status.Active);
            if (stockTake == null) return NotFound();

            foreach (var entry in actualQuantities)
            {
                int consumableId = entry.Key;
                int actualQty = entry.Value;

                var consumable = await _context.Consumables.FindAsync(consumableId);
                if (consumable == null) continue;

                // Create a stock take item
                _context.StockTakeItems.Add(new StockTakeItem
                {
                    StockTakeId = stockTakeId,
                    ConsumableId = consumableId,
                    SystemQuantity = consumable.QuantityOnHand,
                    ActualQuantity = actualQty
                });

                // Optionally update the system quantity to the actual count (if you want auto-adjust)
                // Comment out the next line if you want to keep the old quantity and only record the difference
                // consumable.QuantityOnHand = actualQty;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Stock take recorded.";
            return RedirectToAction(nameof(StockTakes));
        }

        // STOCK TAKE DETAILS
        public async Task<IActionResult> StockTakeDetails(int id)
        {
            var stockTake = await _context.StockTakes
                .Include(s => s.StockTakeItems).ThenInclude(si => si.Consumable)
                .FirstOrDefaultAsync(s => s.Id == id);
            if (stockTake == null) return NotFound();
            return View(stockTake);
        }

        // SOFT DELETE STOCK TAKE
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStockTake(int id)
        {
            var stockTake = await _context.StockTakes.FindAsync(id);
            if (stockTake == null) return NotFound();

            stockTake.IsActive = Status.Inactive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Stock take deactivated.";
            return RedirectToAction(nameof(StockTakes));
        }



        // ==================================================================
        //  LIST RECEIVED ORDERS (optional – you can reuse Orders with a filter)
        // ==================================================================
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
        //  EDIT RECEIVED ORDER – GET
        //  (allows correction of the received quantity)
        // ==================================================================
        [HttpGet]
        public async Task<IActionResult> EditReceivedOrder(int orderId)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.OrderStatus == OrderStatus.Complete && o.IsActive == Status.Active);
            if (order == null) return NotFound();
            return View(order);
        }

        // ==================================================================
        //  EDIT RECEIVED ORDER – POST
        //  (adjusts stock if the quantity is changed)
        // ==================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditReceivedOrder(int orderId, int newQuantityReceived)
        {
            var order = await _context.ConsumableOrders
                .Include(o => o.Consumable)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.OrderStatus == OrderStatus.Complete && o.IsActive == Status.Active);
            if (order == null) return NotFound();

            if (newQuantityReceived < 0)
            {
                TempData["ErrorMessage"] = "Quantity cannot be negative.";
                return RedirectToAction(nameof(EditReceivedOrder), new { orderId });
            }

            // Calculate the difference and adjust stock
            int difference = newQuantityReceived - (order.QuantityReceived ?? 0);
            if (difference != 0)
            {
                order.Consumable.QuantityOnHand += difference;  // if difference is negative, stock decreases
                order.QuantityReceived = newQuantityReceived;
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Received quantity updated. Stock adjusted.";
            }
            return RedirectToAction(nameof(ReceivedOrders));
        }

        // ==================================================================
        //  SOFT DELETE RECEIVED ORDER – POST
        //  (deactivates the order and reverses its stock addition)
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
        //  (reactivates the order and re‑adds the stock)
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

    }
}