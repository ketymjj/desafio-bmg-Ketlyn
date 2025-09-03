// Importações necessárias para o funcionamento do controller
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Data;
using Shared.Security.Interfaces;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace StockService.SalesService.Controllers
{
    // Define a rota base da API: api/orders
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        // Injeções de dependência
        private readonly AppDbContext _context;                  // Contexto do banco de dados
        private readonly ILogger<OrdersController> _logger;      // Logger para registrar erros e informações
        private readonly IJwtTokenService _jwtValidator;         // Serviço para validar tokens JWT

        // Construtor para injetar dependências
        public OrdersController(
            AppDbContext context,
            ILogger<OrdersController> logger,
            IJwtTokenService jwtValidator)
        {
            _context = context;
            _logger = logger;
            _jwtValidator = jwtValidator;
        }

        // ================== ENDPOINT GET TODOS OS PEDIDOS ==================
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)] // Resposta 200 se sucesso
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
        {
            // 🔑 Valida token da requisição
            var principal = ValidateRequestToken();
            if (principal == null)
                return Unauthorized("Token inválido ou não informado");

            try
            {
                // Retorna todos os pedidos sem rastrear (melhor performance em leitura)
                return await _context.Orders
                    .AsNoTracking()
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                // Loga erro no servidor
                _logger.LogError(ex, "Erro ao buscar pedidos");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        // ================== ENDPOINT GET PEDIDO POR ID ==================
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]     // Resposta 200 se sucesso
        [ProducesResponseType(StatusCodes.Status404NotFound)] // Resposta 404 se não encontrado
        public async Task<ActionResult<Order>> GetOrder(int id)
        {
            // 🔑 Valida token da requisição
            var principal = ValidateRequestToken();
            if (principal == null)
                return Unauthorized("Token inválido ou não informado");
       
            try
            {
                // Busca pedido pelo ID
                var order = await _context.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == id);

                // Retorna 404 se não encontrado, caso contrário 200 + objeto
                return order == null ? NotFound() : Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao buscar pedido {id}");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        // ================== ENDPOINT CRIAR PEDIDOS ==================
        [HttpPost]
        [Authorize] // Exige autenticação
        [ProducesResponseType(StatusCodes.Status201Created)] // Resposta 201 se criado
        [ProducesResponseType(StatusCodes.Status400BadRequest)] // Resposta 400 se dados inválidos
        public async Task<ActionResult<IEnumerable<Order>>> PostOrder([FromBody] List<Order> orders)
        {
            // 🔑 Valida token da requisição
            var principal = ValidateRequestToken();
            if (principal == null)
                return Unauthorized("Token inválido ou não informado");

            try
            {
                // Verifica se o modelo recebido é válido
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Itera sobre cada pedido recebido e salva no banco
                foreach (var order in orders)
                {
                    var orderToSave = new Order
                    {
                        CustomerId = order.CustomerId,
                        TotalAmount = order.TotalAmount,
                        OrderDate = DateTime.UtcNow // Data de criação é sempre UTC
                    };

                    _context.Orders.Add(orderToSave);
                    await _context.SaveChangesAsync();

                    // Criação de um evento (pode ser usado para mensageria futuramente)
                    var orderCreatedEvent = new
                    {
                        OrderId = orderToSave.Id,
                        CustomerId = orderToSave.CustomerId,
                        TotalAmount = orderToSave.TotalAmount
                    };

                    // Loga informação de criação
                    _logger.LogInformation($"Pedido {orderToSave.Id} criado com sucesso");
                }

                // Retorna Created (201) com os pedidos enviados
                return Created("api/orders", orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar pedidos");
                return StatusCode(500, "Erro interno ao processar o pedido");
            }
        }
        
        // ================== MÉTODO AUXILIAR ==================
        // 🔑 Valida o token JWT presente no cabeçalho "Authorization"
        private ClaimsPrincipal? ValidateRequestToken()
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return null;

            // Remove "Bearer " do início e valida token
            var token = authHeader.Substring("Bearer ".Length).Trim();
            return _jwtValidator.ValidateToken(token);
        }
    }
}
