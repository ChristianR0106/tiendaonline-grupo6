using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using StackExchange.Redis;
using TiendaOnline.Data;
using TiendaOnline.Models;
using TiendaOnline.Services;

namespace TiendaOnline.Controllers
{
    [Route("Cart")]
    public class CartController : Controller
    {
        private static readonly AmazonCloudWatchClient _cwClient = new AmazonCloudWatchClient(RegionEndpoint.USEast2);
        private readonly CartService _cartService;
        private readonly IConnectionMultiplexer _redis;

        public CartController(CartService cartService, IConnectionMultiplexer redis)
        {
            _cartService = cartService;
            _redis = redis;
        }

        // Muestra la vista del carrito en /Cart o /Cart/Index
        // La vista Views/Cart/Index.cshtml debe empezar con:
        // @model TiendaOnline.Models.CartViewModel
        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var cartModel = await _cartService.ViewAsync(HttpContext.Session);

            // No se redirige si está vacío (causaría un bucle infinito).
            // La vista se encarga de mostrar "Tu carrito está vacío".
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
                _cartService.Add(HttpContext.Session, targetId, quantity <= 0 ? 1 : quantity);

                // --- LÓGICA DE ACTIVIDAD EN REDIS (ActiveCarts - Hash SHA256) ---
                try
                {
                    // Hash SHA-256 del SessionId (nunca guardar el SessionId tal cual)
                    string sessionHash = GetSessionHash(HttpContext.Session.Id);
                    var db = _redis.GetDatabase();

                    // Anota la sesión en Redis con TTL de 5 minutos
                    await db.StringSetAsync($"active_cart:{sessionHash}", DateTime.UtcNow.ToString("o"), TimeSpan.FromMinutes(5));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al registrar actividad de carrito en Redis: {ex.Message}");
                }
            }

            try
            {
                var metricRequest = new PutMetricDataRequest
                {
                    Namespace = "URPOnline",
                    MetricData = new List<MetricDatum>
                    {
                        new MetricDatum
                        {
                            MetricName = "ActiveCarts",
                            Value = 1,
                            Unit = StandardUnit.Count,
                            Dimensions = new List<Dimension>
                            {
                                new Dimension { Name = "Environment", Value = "Development" }
                            }
                        },
                        new MetricDatum
                        {
                            MetricName = "CartItemsAdded",
                            Value = quantity > 0 ? quantity : 1,
                            Unit = StandardUnit.Count,
                            Dimensions = new List<Dimension>
                            {
                                new Dimension { Name = "Environment", Value = "Development" }
                            }
                        }
                    }
                };

                await _cwClient.PutMetricDataAsync(metricRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registrando métricas: {ex.Message}");
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

        // Método privado helper para hashear el ID de sesión
        private static string GetSessionHash(string sessionId)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sessionId ?? Guid.NewGuid().ToString()));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    [Route("Checkout")]
    public class CheckoutController : Controller
    {
        private readonly CartService _cartService;
        private readonly ShopDbContext _db;
        private readonly BusinessEvents _events;

        public CheckoutController(CartService cartService, ShopDbContext db, BusinessEvents events)
        {
            _cartService = cartService;
            _db = db;
            _events = events;
        }

        // Muestra el formulario de checkout en /Checkout
        // La vista Views/Checkout/Index.cshtml usa @model CheckoutInput
        // y lee el carrito desde ViewBag.Cart
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

            // Si falla la validación, vuelve a mostrar el formulario con los errores
            if (!ModelState.IsValid)
            {
                ViewBag.Cart = cartModel;
                return View(input);
            }

            var order = new TiendaOnline.Models.Order
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

            // Registro de evento de negocio (si falla, no se cancela el pedido)
            try
            {
                await _events.RecordAsync("order_created", orderId: order.Id, amount: order.Total);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registrando evento order_created: {ex.Message}");
            }

            // Vacía el carrito
            _cartService.Clear(HttpContext.Session);

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
                PaymentId = null // Aún no hay integración real con Mercado Pago
            });
        }
    }
}