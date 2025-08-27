// Services/PromocaoService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Data;
using Shared.Interface;
using Shared.Models.PromocaoVendas;

namespace PromocaoiPhone.Services
{
    public class PromocaoService : IPromocaoService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PromocaoService> _logger;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public PromocaoService(AppDbContext context, ILogger<PromocaoService> logger)
        {
            _context = context;
            _logger = logger;
        }

       public async Task<RespostaVenda> ComprariPhone(int produtoId, int quantidade)
       {
            if (quantidade <= 0)
                return new RespostaVenda { Sucesso = false, Mensagem = "Quantidade deve ser maior que zero" };
        
            await _semaphore.WaitAsync();
        
            try
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
        
                // Obter o produto
                var produto = await _context.Products.FirstOrDefaultAsync(p => p.Id == produtoId);
                if (produto == null)
                    return new RespostaVenda { Sucesso = false, Mensagem = "Produto não encontrado" };
        
                // Só aplica limite de 100 unidades/hora para iPhone
                bool isIphone = produto.Name.Contains("IPhone", StringComparison.OrdinalIgnoreCase);
        
                if (isIphone)
                {
                    var promocao = await _context.Promocoes.FirstOrDefaultAsync();
                    if (promocao == null)
                        throw new InvalidOperationException("Promoção não encontrada");
        
                    var horaAtual = DateTime.UtcNow.Hour;
                    if (promocao.Hora != horaAtual)
                    {
                        promocao.Hora = horaAtual;
                        promocao.UnidadesDisponiveis = 100;
                        promocao.UltimaAtualizacao = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
        
                    if (promocao.UnidadesDisponiveis < quantidade)
                    {
                        return new RespostaVenda
                        {
                            Sucesso = false,
                            Mensagem = $"Unidades insuficientes. Disponível: {promocao.UnidadesDisponiveis}",
                            UnidadesDisponiveis = promocao.UnidadesDisponiveis
                        };
                    }
        
                    promocao.UnidadesDisponiveis -= quantidade;
                    promocao.UltimaAtualizacao = DateTime.UtcNow;
        
                    _context.Vendas.Add(new Venda
                    {
                        Quantidade = quantidade,
                        Hora = promocao.Hora,
                        DataVenda = DateTime.UtcNow
                    });
                }
                else
                {
                    // Para outros produtos, pode vender normalmente
                    produto.StockQuantity -= quantidade;
                }
        
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
        
                return new RespostaVenda
                {
                    Sucesso = true,
                    Mensagem = "Compra realizada com sucesso!",
                    UnidadesVendidas = quantidade,
                    UnidadesDisponiveis = isIphone ? (await ObterDisponibilidadeAtual()) : -1
                };
            }
            finally
            {
                _semaphore.Release();
            }
        }


        public async Task<int> ObterDisponibilidadeAtual()
        {
            var promocao = await _context.Promocoes.FirstOrDefaultAsync();
            if (promocao == null) return 0;

            // Verificar se precisa atualizar para uma nova hora
            var horaAtual = DateTime.UtcNow.Hour;
            if (promocao.Hora != horaAtual)
            {
                promocao.Hora = horaAtual;
                promocao.UnidadesDisponiveis = 100;
                promocao.UltimaAtualizacao = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return promocao.UnidadesDisponiveis;
        }
    }
}