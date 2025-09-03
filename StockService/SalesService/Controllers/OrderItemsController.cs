using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Models;
using Shared.Security.Interfaces;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Shared.Interface;

namespace StockService.SalesService.Controllers
{
    // Controller responsável por gerenciar os itens dos pedidos
    [Route("api/[controller]")]
    [ApiController]
    public class OrderItemsController : ControllerBase
    {
        private readonly AppDbContext _context;               // Contexto do banco de dados (EF Core)
        private readonly ILogger<OrderItemsController> _logger; // Logger para registrar eventos e erros
        private readonly IJwtTokenService _jwtValidator;      // Serviço de validação do token JWT
        private readonly IPromocaoService _promocaoService;   // Serviço de promoção (para iPhones)

        // Construtor com injeção de dependência
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

        // 📌 GET: api/orderitems/{orderId}
        // Retorna todos os itens de um pedido específico
        [HttpGet("{orderId}")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<OrderItem>>> GetItemsByOrder(int orderId)
        {
            var principal = ValidateRequestToken();
            if (principal == null)
                return Unauthorized("Token inválido ou não informado");

            // Obtém o usuário logado pelo claim no token
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("Usuário não identificado no token");

            try
            {
                // Verifica se o pedido existe
                var order = await _context.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == orderId);

                if (order == null)
                    return NotFound("Pedido não encontrado para este usuário.");

                // Busca todos os itens relacionados ao pedido, incluindo o produto
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

        // 📌 POST: api/orderitems
        // Adiciona itens a um pedido (inclui validação da promoção de iPhone)
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
                // Busca os produtos envolvidos no pedido
                var productIds = items.Select(i => i.ProductId).Distinct().ToList();
                var products = await _context.Products
                    .Where(p => productIds.Contains(p.Id))
                    .ToListAsync();

                // Verifica se existe algum iPhone nos itens
                var iPhoneItems = items
                    .Where(i => products.Any(p => p.Id == i.ProductId && p.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (iPhoneItems.Any())
                {
                    // Soma total de iPhones comprados
                    var totalIphones = iPhoneItems.Sum(i => i.Quantity);

                    // Obtém o ID do produto iPhone
                    var iphoneProductId = products.First(p => p.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase)).Id;

                    // Valida a compra na promoção
                    var resultadoPromocao = await _promocaoService.ComprariPhone(iphoneProductId, totalIphones);

                    if (!resultadoPromocao.Sucesso)
                        return BadRequest($"Promoção iPhone: {resultadoPromocao.Mensagem}");
                }

                // Transação para garantir atomicidade
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    foreach (var item in items)
                    {
                        // Verifica se o pedido existe
                        var order = await _context.Orders.FindAsync(item.OrderId);
                        if (order == null)
                            return BadRequest($"Pedido {item.OrderId} não existe.");

                        // Verifica se o produto existe
                        var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                        if (product == null)
                            return BadRequest($"Produto {item.ProductId} não existe.");

                        bool isIphone = product.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase);

                        // Verifica estoque
                        if (product.StockQuantity < item.Quantity)
                            return BadRequest($"Estoque insuficiente para o produto {product.Name}.");

                        // Atualiza estoque
                        product.StockQuantity -= item.Quantity;

                        // Ajusta preço do item, caso não informado
                        if (item.UnitPrice <= 0)
                            item.UnitPrice = product.Price;

                        // Validação de preço correto
                        if (product.Price != item.UnitPrice)
                            return BadRequest($"Produto {item.ProductId} com valor incorreto.");

                        // Calcula preço total
                        item.TotalPrice = item.UnitPrice * item.Quantity;

                        // Adiciona item ao pedido
                        _context.OrderItems.Add(item);

                        _logger.LogInformation($"Item do pedido {order.Id} criado com sucesso");
                    }

                    // Salva alterações
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Retorna Created com os itens adicionados
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

        // 📌 DELETE: api/orderitems/{id}
        // Remove um item de pedido
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

                // Se for iPhone (promoção), não devolve estoque normal
                if (item.ProductId != 1) // OBS: aqui está hardcoded (iPhone = ID 1)
                {
                    var product = item.Product;
                    if (product != null)
                        product.StockQuantity += item.Quantity;
                }

                // Atualiza valor total do pedido
                var order = await _context.Orders.FindAsync(item.OrderId);
                if (order != null)
                    order.TotalAmount -= item.Quantity * item.UnitPrice;

                // Remove o item
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

        // 📌 GET: api/orderitems/promotion/availability
        // Obtém a quantidade atual de iPhones disponíveis na promoção
        [HttpGet("Promotion/Availability")]
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

        // 📌 Método auxiliar para validar o token JWT
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
