using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Data;
using System.Security.Claims;
using Shared.Security.Interfaces;
using Microsoft.AspNetCore.Authorization;
using StockService.ModelDto;

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
        public async Task<ActionResult<Product>> PostProduct([FromForm] ProductCreateDto dto)
        {
            try
            {
                // 🔑 Validação do token
                var principal = ValidateRequestToken();
                if (principal == null)
                    return Unauthorized("Token inválido ou não informado");

                // Validação do modelo
                if (!ModelState.IsValid) return BadRequest(ModelState);

                // Salvar imagem no servidor
               string? imagePath = null;
               if (dto.Image != null && dto.Image.Length > 0)
               {
                   var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");
                   if (!Directory.Exists(uploadsFolder))
                       Directory.CreateDirectory(uploadsFolder);
       
                   var fileName = Guid.NewGuid() + Path.GetExtension(dto.Image.FileName);
                   var filePath = Path.Combine(uploadsFolder, fileName);
       
                   using (var stream = new FileStream(filePath, FileMode.Create))
                   {
                       await dto.Image.CopyToAsync(stream);
                   }
       
                   imagePath = "/images/" + fileName; // URL relativa
               }

                var product = new Product
               {
                   Name = dto.Name,
                   Description = dto.Description,
                   Price = dto.Price,
                   StockQuantity = dto.StockQuantity,
                   ImageUrl = imagePath // precisa ter essa coluna na tabela Product
               };

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
       public async Task<IActionResult> PutProduct(int id, [FromForm] ProductCreateDto dto)
        {
            try
            {
                // 🔑 Validação do token
                var principal = ValidateRequestToken();
                if (principal == null)
                    return Unauthorized("Token inválido ou não informado");

                // Busca o produto no banco
                var existingProduct = await _context.Products.FindAsync(id);
                if (existingProduct == null) return NotFound();

                 // Atualizar dados
                existingProduct.Name = dto.Name;
                existingProduct.Price = dto.Price;
                existingProduct.StockQuantity = dto.StockQuantity;
                existingProduct.UpdatedAt = DateTime.UtcNow;

                // Atualiza os valores do produto
                var oldStock = existingProduct.StockQuantity; // exemplo de captura de valor antigo (pode ser usado em auditoria)

              // Se enviou nova imagem, sobrescreve
                if (dto.Image != null && dto.Image.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    var fileName = Guid.NewGuid() + Path.GetExtension(dto.Image.FileName);
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.Image.CopyToAsync(stream);
                    }

                    existingProduct.ImageUrl = "/images/" + fileName;
                }


                _context.Entry(existingProduct).CurrentValues.SetValues(dto);
                await _context.SaveChangesAsync();

                // Captura o ID do usuário autenticado
                var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                _logger.LogInformation($"Produto {id} atualizado pelo usuário {userId}");

                // Retorna 204 NoContent (atualização feita com sucesso, sem corpo na resposta)
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao atualizar produto {id}");
                return StatusCode(500, "Erro interno ao processar a requisição");
            }
        }

        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            try
            {
                var principal = ValidateRequestToken();
                if (principal == null)
                    return Unauthorized("Token inválido ou não informado");
        
                var existingProduct = await _context.Products.FindAsync(id);
                if (existingProduct == null) return NotFound();
        
                // Se tiver imagem salva, apaga do disco também
                if (!string.IsNullOrEmpty(existingProduct.ImageUrl))
                {
                    var imagePath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        existingProduct.ImageUrl.TrimStart('/')
                    );
        
                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }
        
                _context.Products.Remove(existingProduct);
                await _context.SaveChangesAsync();
        
                var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                _logger.LogInformation($"Produto {id} deletado pelo usuário {userId}");
        
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Erro ao deletar produto {id}");
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
