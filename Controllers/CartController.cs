using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TiendaOnline.Data;
using TiendaOnline.Models;
using TiendaOnline.Services;

namespace TiendaOnline.Controllers
{
    [Route("Cart")]
    public class CartController : Controller
    {
        private readonly CartService _cartService;
        private readonly BusinessMetrics _metrics;

        public CartController(CartService cartService, BusinessMetrics metrics)
        {
            _cartService = cartService;
            _metrics = metrics;
        }

        // Muestra la vista del carrito en /Cart o /Cart/Index
        // Views/Cart/Index.cshtml usa @model CartViewModel
        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var cartModel = await _cartService.ViewAsync(HttpContext.Session);
            return View(cartModel);
        }

        // Procesa peticiones enviadas a /Cart/Add o /Cart/AddToCart
        [HttpPost("Add")]
        [HttpPost("AddToCart")]
        public async Task<IActionResult> AddToCart(int productId, int id, int quantity = 1)
        {
            // Captura el ID sin importar si el frontend lo envía como 'productId' o 'id'
            int targetId = productId > 0 ? productId : id;

            if (targetId > 0)
            {
                // Misma regla que CartService.Add (1 a 99), para que la métrica coincida con lo agregado
                int qty = Math.Clamp(quantity, 1, 99);
                _cartService.Add(HttpContext.Session, targetId, qty);

                // Métricas: nunca deben romper la compra
                try
                {
                    // CartItemsAdded: cada "Agregar al carrito"
                    await _metrics.TrackCartItemAddedAsync(qty);

                    // ActiveCarts: anota la sesión (hasheada con SHA-256 dentro de BusinessMetrics)
                    await _metrics.RegisterCartActivityAsync(HttpContext.Session.Id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error registrando métricas del carrito: {ex.Message}");
                }
            }

            // Si es petición AJAX responde JSON
            if (Request.Headers["X-Requested-With"].ToString().Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || Request.HasJsonContentType())
            {
                return Json(new { success = true, message = "Carrito actualizado exitosamente" });
            }

            // Si viene de un formulario HTML común redirige a la vista del carrito
            return RedirectToAction("Index");
        }

        [HttpPost("Remove")]
        public IActionResult Remove(int id)
        {
            _cartService.Remove(HttpContext.Session, id);
            return RedirectToAction("Index");
        }
    }

    [Route("Checkout")]
    public class CheckoutController : Controller
    {
        private readonly CartService _cartService;
        private readonly ShopDbContext _db;
        private readonly BusinessEvents _events;
        private readonly BusinessMetrics _metrics;

        public CheckoutController(CartService cartService, ShopDbContext db, BusinessEvents events, BusinessMetrics metrics)
        {
            _cartService = cartService;
            _db = db;
            _events = events;
            _metrics = metrics;
        }

        // Muestra el formulario de checkout en /Checkout
        // Views/Checkout/Index.cshtml usa @model CheckoutInput y lee el carrito de ViewBag.Cart
        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var cartModel = await _cartService.ViewAsync(HttpContext.Session);

            if (cartModel.Lines == null || cartModel.Lines.Count == 0)
            {
                return RedirectToAction("Index", "Cart");
            }

            ViewBag.Cart = cartModel;
            return View(new CheckoutInput());
        }

        // Recibe el formulario "Confirmar pedido": crea el pedido en PENDIENTE_PAGO
        [HttpPost("")]
        [HttpPost("Index")]
        [HttpPost("Process")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(CheckoutInput input)
        {
            var cartModel = await _cartService.ViewAsync(HttpContext.Session);

            if (cartModel.Lines == null || cartModel.Lines.Count == 0)
            {
                return RedirectToAction("Index", "Cart");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Cart = cartModel;
                return View(input);
            }

            var order = new Order
            {
                CustomerName = input.CustomerName,
                Email = input.Email,
                Address = input.Address,
                CreatedUtc = DateTime.UtcNow,
                Status = "PENDIENTE_PAGO",
                Total = cartModel.Total,
                Items = cartModel.Lines.Select(l => new OrderItem
                {
                    ProductId = l.Product.Id,
                    ProductName = l.Product.Name,
                    Quantity = l.Quantity,
                    UnitPrice = l.Product.Price
                }).ToList()
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            // Métrica OrdersCreated y evento de negocio (si fallan, no se cancela el pedido)
            try
            {
                await _metrics.TrackOrderCreatedAsync(order.Total);
                await _events.RecordAsync("order_created", orderId: order.Id, amount: order.Total);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registrando métricas/evento del pedido: {ex.Message}");
            }

            _cartService.Clear(HttpContext.Session);

            // TODO (3d): aquí se pedirá el link de pago a Mercado Pago y se redirigirá al cliente
            return RedirectToAction("Success", new { id = order.Id });
        }

        // Vista de confirmación: /Checkout/Success?id=123
        [HttpGet("Success")]
        public async Task<IActionResult> Success(int id)
        {
            var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
            if (order is null)
            {
                return RedirectToAction("Index", "Home");
            }

            return View(new OrderResult
            {
                Id = order.Id,
                Status = order.Status,
                PaymentId = null
            });
        }
    }
}