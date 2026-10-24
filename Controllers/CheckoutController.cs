using MercadoPago.Client.Preference;
using MercadoPago.Client.Payment;
using MercadoPago.Config;
using MercadoPago.Resource.Preference;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace URPOnline.Controllers
{
    public class CheckoutController : Controller
    {
        private readonly IAmazonSecretsManager _secretsManager;
        private readonly IOrderService _orderService;
        private readonly BusinessMetrics _businessMetrics;

        public CheckoutController(IAmazonSecretsManager secretsManager, IOrderService orderService, BusinessMetrics businessMetrics)
        {
            _secretsManager = secretsManager;
            _orderService = orderService;
            _businessMetrics = businessMetrics;
        }

        // Leer AccessToken desde AWS Secrets Manager
        private async Task<string> GetMercadoPagoTokenAsync()
        {
            var response = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = "URPOnline/MercadoPagoAccessToken"
            });
            return response.SecretString;
        }

        // 3d. Guardar Pedido y Redirigir a Mercado Pago
        [HttpPost]
        public async Task<IActionResult> ProcessCheckout(OrderRequest model)
        {
            // 1. Guardar en estado PENDIENTE_PAGO
            var order = await _orderService.CreateOrderAsync(model, OrderStatus.PENDIENTE_PAGO);

            // 2. Configurar Token de Mercado Pago desde AWS SDK
            MercadoPagoConfig.AccessToken = await GetMercadoPagoTokenAsync();

            // 3. Crear Preferencia de Pago
            var client = new PreferenceClient();
            var preference = await client.CreateAsync(new PreferenceRequest
            {
                Items = order.Items.Select(i => new PreferenceItemRequest
                {
                    Title = i.ProductName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    CurrencyId = "PEN"
                }).ToList(),
                ExternalReference = order.Id.ToString(),
                BackUrls = new PreferenceBackUrlsRequest
                {
                    Success = $"{Request.Scheme}://{Request.Host}/Checkout/Confirmation",
                    Failure = $"{Request.Scheme}://{Request.Host}/Checkout/Confirmation",
                    Pending = $"{Request.Scheme}://{Request.Host}/Checkout/Confirmation"
                },
                AutoReturn = "approved"
            });

            // 4. Redirigir al cliente al checkout de Mercado Pago
            return Redirect(preference.InitPoint);
        }

        // 3e. Confirmación Servidor-Servidor al Volver
        [HttpGet]
        public async Task<IActionResult> Confirmation([FromQuery] string payment_id, [FromQuery] string external_reference)
        {
            if (string.IsNullOrEmpty(payment_id) || string.IsNullOrEmpty(external_reference))
                return RedirectToAction("Index", "Home");

            Guid orderId = Guid.Parse(external_reference);
            var order = await _orderService.GetOrderByIdAsync(orderId);

            // Protección contra duplicados (Idempotencia): Si ya fue procesado, no repetir
            if (order.Status == OrderStatus.PAGADO)
            {
                return View("Success", order);
            }

            // Validar Estado Real del Pago en los Servidores de Mercado Pago
            MercadoPagoConfig.AccessToken = await GetMercadoPagoTokenAsync();
            var paymentClient = new PaymentClient();
            var payment = await paymentClient.GetAsync(long.Parse(payment_id));

            if (payment.Status == "approved")
            {
                order.Status = OrderStatus.PAGADO;
                order.PaymentId = payment_id;
                await _orderService.UpdateOrderAsync(order);

                // Métrica de venta confirmada
                await _businessMetrics.TrackOrderCreatedAsync(order.TotalAmount);

                return View("Success", order);
            }
            else
            {
                order.Status = OrderStatus.PENDIENTE_PAGO;
                await _orderService.UpdateOrderAsync(order);

                ViewBag.ErrorMessage = $"El pago fue rechazado o no se completó. Estado: {payment.StatusDetail}";
                return View("Rejected", order);
            }
        }
    }
}