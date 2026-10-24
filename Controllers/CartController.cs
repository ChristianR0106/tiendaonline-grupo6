using Microsoft.AspNetCore.Mvc;
using TiendaOnline.Data;
using TiendaOnline.Services;

namespace TiendaOnline.Controllers;

public class CartController(
    CartService cart,
    ShopDbContext db,
    BusinessEvents events,
    BusinessMetrics metrics) : Controller 
{
    public async Task<IActionResult> Index() => View(await cart.ViewAsync(HttpContext.Session));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int id, int quantity = 1) 
    {
        if (await db.Products.FindAsync(id) is { Stock: > 0 } p) 
        {
            int addedQuantity = Math.Min(quantity, p.Stock);
            cart.Add(HttpContext.Session, id, addedQuantity);
            await events.RecordAsync("cart_add", productId: id);

            // --- BLOQUE 3a: Métricas de AWS SDK y Redis ---
            await metrics.TrackCartItemAddedAsync(addedQuantity);
            await metrics.RegisterCartActivityAsync(HttpContext.Session.Id);
            // ----------------------------------------------

            TempData["Message"] = "Producto agregado al carrito";
        }
        return RedirectToAction("Index", "Home");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Remove(int id) 
    { 
        cart.Remove(HttpContext.Session, id); 
        return RedirectToAction("Index"); 
    }
}