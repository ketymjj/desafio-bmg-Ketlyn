using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Models;
using Shared.Security.Interfaces;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Shared.Interface;
using Shared.Models.PromocaoVendas;
using Microsoft.Extensions.Logging;

namespace StockService.SalesService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrderItemsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<OrderItemsController> _logger;
        private readonly IJwtTokenService _jwtValidator;
        private readonly IPromocaoService _promocaoService;

        public OrderItemsController(
            AppDbContext context,
            ILogger<OrderItemsController> logger,
            IJwtTokenService jwtValidator,
            IPromocaoService promocaoService)
        {
            _context = context;
            _logger = logger;
            _jwtValidator = jwtValidator;
            _promocaoService = promocaoService;
        }

        // GET: api/orderitems/{orderId}
        [HttpGet("{orderId}")]
        public async Task<ActionResult<IEnumerable<OrderItem>>> GetItemsByOrder(int orderId)
        {
            var principal = ValidateRequestToken();
            if (principal == null)
                return Unauthorized("Token inválido ou não informado");

            try
            {
                var items = await _context.OrderItems
                    .Include(i => i.Product)
                    .Where(i => i.OrderId == orderId)
                    .AsNoTracking()
                    .ToListAsync();

                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao buscar itens do pedido {orderId}");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        // POST: api/orderitems
      [HttpPost]
        [Authorize]
        public async Task<ActionResult<IEnumerable<OrderItem>>> AddOrderItems([FromBody] List<OrderItem> items)
        {
            var principal = ValidateRequestToken();
            if (principal == null)
                return Unauthorized("Token inválido ou não informado");
        
            if (items == null || !items.Any())
                return BadRequest("Nenhum item enviado.");
        
            try
            {
                // Buscar todos os produtos enviados
                var productIds = items.Select(i => i.ProductId).Distinct().ToList();
                var products = await _context.Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToListAsync();
        
                // Verificar se há produtos iPhone na promoção pelo nome
                var iPhoneItems = items
                    .Where(i => products.Any(p => p.Id == i.ProductId && p.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
        
                if (iPhoneItems.Any())
                {
                    var totalIphones = iPhoneItems.Sum(i => i.Quantity);
        
                    // Pegar ProductId real do iPhone
                    var iphoneProductId = products.First(p => p.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase)).Id;
        
                    // Validar promoção
                    var resultadoPromocao = await _promocaoService.ComprariPhone(iphoneProductId, totalIphones);
        
                    if (!resultadoPromocao.Sucesso)
                        return BadRequest($"Promoção iPhone: {resultadoPromocao.Mensagem}");
                }
        
                using var transaction = await _context.Database.BeginTransactionAsync();
        
                try
                {
                    foreach (var item in items)
                    {
                        var order = await _context.Orders.FindAsync(item.OrderId);
                        if (order == null)
                            return BadRequest($"Pedido {item.OrderId} não existe.");
        
                        var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                        if (product == null)
                            return BadRequest($"Produto {item.ProductId} não existe.");
        
                        bool isIphone = product.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase);
        
                        // Para todos os produtos, inclusive iPhone, decrementar estoque
                        if (product.StockQuantity < item.Quantity)
                            return BadRequest($"Estoque insuficiente para o produto {product.Name}.");
        
                        product.StockQuantity -= item.Quantity;
        
                        // Validação de preço
                        if (item.UnitPrice <= 0)
                            item.UnitPrice = product.Price;
        
                        if (product.Price != item.UnitPrice)
                            return BadRequest($"Produto {item.ProductId} com valor incorreto.");
        
                        item.TotalPrice = item.UnitPrice * item.Quantity;
        
                        _context.OrderItems.Add(item);
        
                        _logger.LogInformation($"Item do pedido {order.Id} criado com sucesso");
                    }
        
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
        
                    return Created("api/orderitems", items);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Erro ao adicionar itens ao pedido");
                    return StatusCode(500, "Erro interno ao processar os itens");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar pedido com promoção");
                return StatusCode(500, "Erro interno ao processar a promoção");
            }
        }



        // DELETE: api/orderitems/{id}
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteOrderItem(int id)
        {
            var principal = ValidateRequestToken();
            if (principal == null)
                return Unauthorized("Token inválido ou não informado");

            try
            {
                var item = await _context.OrderItems
                    .Include(i => i.Product)
                    .FirstOrDefaultAsync(i => i.Id == id);
                
                if (item == null)
                    return NotFound();

                // Se for iPhone, não devolver ao estoque normal (já que foi vendido na promoção)
                if (item.ProductId != 1) // iPhone tem ProductId 1
                {
                    var product = item.Product;
                    if (product != null)
                        product.StockQuantity += item.Quantity;
                }

                var order = await _context.Orders.FindAsync(item.OrderId);
                if (order != null)
                    order.TotalAmount -= item.Quantity * item.UnitPrice;

                _context.OrderItems.Remove(item);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao remover item {id}");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        // GET: api/orderitems/promocao/disponibilidade
        [HttpGet("promocao/disponibilidade")]
        public async Task<ActionResult<int>> ObterDisponibilidadeiPhone()
        {
            try
            {
                var disponibilidade = await _promocaoService.ObterDisponibilidadeAtual();
                return Ok(disponibilidade);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao obter disponibilidade do iPhone");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }
        
        private ClaimsPrincipal? ValidateRequestToken()
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return null;

            var token = authHeader.Substring("Bearer ".Length).Trim();
            return _jwtValidator.ValidateToken(token);
        }
    }
}