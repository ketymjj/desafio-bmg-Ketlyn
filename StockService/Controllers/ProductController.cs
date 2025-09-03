using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Data;
using System.Security.Claims;
using Shared.Security.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace StockService.Controllers
{
    // Define a rota base para este controller como "api/product"
    [Route("api/[controller]")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly AppDbContext _context; // Contexto do banco de dados (EF Core)
        private readonly ILogger<ProductController> _logger; // Logger para registrar logs
        private readonly IJwtTokenService _tokenValidator; // Serviço para validar tokens JWT

        // Injeção de dependências via construtor
        public ProductController(
            AppDbContext context,
            ILogger<ProductController> logger,
            IJwtTokenService tokenValidator) 
        {
            _context = context;
            _logger = logger;
            _tokenValidator = tokenValidator;
        }

        // 📌 GET: api/product
        // Retorna a lista de todos os produtos
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            try
            {
                // Consulta assíncrona sem rastreamento (melhor performance para leitura)
                return await _context.Products.AsNoTracking().ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar produtos");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        // 📌 GET: api/product/{id}
        // Retorna um único produto pelo ID
        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProduct(int id)
        {
            try
            {
                var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
                return product == null ? NotFound() : Ok(product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao buscar produto {id}");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        // 📌 POST: api/product
        // Cria um novo produto (requer autenticação)
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<Product>> PostProduct([FromBody] Product product)
        {
            try
            {
                // 🔑 Validação do token
                var principal = ValidateRequestToken();
                if (principal == null)
                    return Unauthorized("Token inválido ou não informado");

                // Validação do modelo
                if (!ModelState.IsValid) return BadRequest(ModelState);

                // Adiciona o produto ao banco
                _context.Products.Add(product);
                await _context.SaveChangesAsync();

                // Captura o ID do usuário autenticado
                var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                _logger.LogInformation($"Produto {product.Id} criado pelo usuário {userId}");

                // Retorna 201 Created com a rota do novo recurso
                return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar produto");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        // 📌 PUT: api/product/{id}
        // Atualiza um produto existente (requer autenticação)
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> PutProduct(int id, [FromBody] Product product)
        {
            try
            {
                // 🔑 Validação do token
                var principal = ValidateRequestToken();
                if (principal == null)
                    return Unauthorized("Token inválido ou não informado");

                // Valida se o ID da rota corresponde ao do produto enviado
                if (id != product.Id) return BadRequest("ID do produto não corresponde");
                if (!ModelState.IsValid) return BadRequest(ModelState);

                // Busca o produto no banco
                var existingProduct = await _context.Products.FindAsync(id);
                if (existingProduct == null) return NotFound();

                // Atualiza os valores do produto
                var oldStock = existingProduct.StockQuantity; // exemplo de captura de valor antigo (pode ser usado em auditoria)
                _context.Entry(existingProduct).CurrentValues.SetValues(product);
                await _context.SaveChangesAsync();

                // Captura o ID do usuário autenticado
                var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                _logger.LogInformation($"Produto {id} atualizado pelo usuário {userId}");

                // Retorna 204 NoContent (atualização feita com sucesso, sem corpo na resposta)
                return NoContent();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, $"Conflito ao atualizar produto {id}");
                return StatusCode(409, "Conflito de concorrência detectado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao atualizar produto {id}");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        // 📌 Método helper para validar o token manualmente
        private ClaimsPrincipal? ValidateRequestToken()
        {
            // Obtém o header "Authorization"
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return null;

            // Extrai apenas o token
            var token = authHeader.Substring("Bearer ".Length).Trim();

            // Valida usando o serviço injetado
            return _tokenValidator.ValidateToken(token);
        }
    }
}
