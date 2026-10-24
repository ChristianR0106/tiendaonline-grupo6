using Microsoft.AspNetCore.Mvc;
using TiendaOnline.Data;
using TiendaOnline.Services;
namespace TiendaOnline.Controllers;
public class CartController(CartService cart,ShopDbContext db,BusinessEvents events):Controller {
 public async Task<IActionResult> Index()=>View(await cart.ViewAsync(HttpContext.Session));
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> Add(int id,int quantity=1) {
  if(await db.Products.FindAsync(id) is { Stock: > 0 } p) {
   cart.Add(HttpContext.Session,id,Math.Min(quantity,p.Stock));
   await events.RecordAsync("cart_add",productId:id);
   TempData["Message"]="Producto agregado al carrito";
  }
  return RedirectToAction("Index","Home");
 }
 [HttpPost,ValidateAntiForgeryToken]
 public IActionResult Remove(int id) { cart.Remove(HttpContext.Session,id); return RedirectToAction("Index"); }
}
[HttpPost]
public async Task<IActionResult> AddToCart(Guid productId, int quantity = 1)
{
    // 1. Agregar producto a la sesión/base de datos
    await _cartService.AddItemAsync(productId, quantity);

    // 2. Obtener SessionId de la cookie HTTP
    string sessionId = HttpContext.Session.Id;

    // 3. Invocar métricas SDK
    await _businessMetrics.TrackCartItemAddedAsync(quantity);
    await _businessMetrics.RegisterCartActivityAsync(sessionId);

    return RedirectToAction("Index");
}
