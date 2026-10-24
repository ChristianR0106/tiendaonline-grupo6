using System.ComponentModel.DataAnnotations;
namespace TiendaOnline.Models;
public class CartItem { public int ProductId { get; set; } public int Quantity { get; set; } }
public class CartLine { public Product Product { get; set; } = null!; public int Quantity { get; set; } public decimal Subtotal => Product.Price * Quantity; }
public class CartViewModel { public List<CartLine> Lines { get; set; } = new(); public decimal Total => Lines.Sum(x => x.Subtotal); }
public class CheckoutInput {
 [Required(ErrorMessage="Ingrese su nombre")] public string CustomerName { get; set; } = "";
 [Required, EmailAddress] public string Email { get; set; } = "";
 [Required(ErrorMessage="Ingrese dirección de entrega")] public string Address { get; set; } = "";
}
public class OrderResult
{
    public int Id { get; set; }
    public string Status { get; set; } = "PENDIENTE_PAGO";
    public string? PaymentId { get; set; }
}
