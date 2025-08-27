using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Data;
using Shared.Security.Interfaces;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace StockService.SalesService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<OrdersController> _logger;
        private readonly IJwtTokenService _jwtValidator;

        public OrdersController(
            AppDbContext context,
            ILogger<OrdersController> logger,
            IJwtTokenService jwtValidator)
        {
            _context = context;
            _logger = logger;
            _jwtValidator = jwtValidator;
        }



        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
        {
             var principal = ValidateRequestToken();

                if (principal == null)
                    return Unauthorized("Token inválido ou não informado");
            try
            {
                return await _context.Orders
                    .AsNoTracking()
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar pedidos");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Order>> GetOrder(int id)
        {
           var principal = ValidateRequestToken();

                if (principal == null)
                    return Unauthorized("Token inválido ou não informado");
       
        try
            {
                var order = await _context.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == id);

                return order == null ? NotFound() : Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao buscar pedido {id}");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        [HttpPost]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<IEnumerable<Order>>> PostOrder([FromBody] List<Order> orders)
        {
           var principal = ValidateRequestToken();

                if (principal == null)
                    return Unauthorized("Token inválido ou não informado");

            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                foreach (var order in orders)
                {
                    var orderToSave = new Order
                    {
                        CustomerId = order.CustomerId,
                        TotalAmount = order.TotalAmount,
                        OrderDate = DateTime.UtcNow
                    };

                    _context.Orders.Add(orderToSave);
                    await _context.SaveChangesAsync();

                    var orderCreatedEvent = new
                    {
                        OrderId = orderToSave.Id,
                        CustomerId = orderToSave.CustomerId,
                        TotalAmount = orderToSave.TotalAmount
                    };

                    _logger.LogInformation($"Pedido {orderToSave.Id} criado com sucesso");
                }

                return Created("api/orders", orders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar pedidos");
                return StatusCode(500, "Erro interno ao processar o pedido");
            }
        }
        
          // 🔑 Método helper para validar token
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
