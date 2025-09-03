// Services/PromocaoService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Data;
using Shared.Interface;
using Shared.Models.PromocaoVendas;
using System.Threading;

namespace PromocaoiPhone.Services
{
    // Serviço para gerenciar promoções de vendas, especialmente iPhones
    public class PromocaoService : IPromocaoService
    {
        private readonly AppDbContext _context; // Contexto do banco de dados (EF Core)
        private readonly ILogger<PromocaoService> _logger; // Logger para registrar eventos e erros

        // SemaphoreSlim para limitar o acesso concorrente a 1 thread por vez
        // 🔑 Garante que duas vendas não sejam processadas ao mesmo tempo, evitando overselling
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        // Injeção de dependências via construtor
        public PromocaoService(AppDbContext context, ILogger<PromocaoService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Método para comprar iPhones com limitação de unidades por hora
        public async Task<RespostaVenda> ComprariPhone(int produtoId, int quantidade)
        {
            // Validação de quantidade
            if (quantidade <= 0)
                return new RespostaVenda { Sucesso = false, Mensagem = "Quantidade deve ser maior que zero" };

            // 🔑 Espera até que o semáforo permita entrar (controla concorrência)
            await _semaphore.WaitAsync();

            try
            {
                // Cria uma transação para garantir atomicidade das operações no banco
                using var transaction = await _context.Database.BeginTransactionAsync();

                // Busca o produto pelo ID
                var produto = await _context.Products.FirstOrDefaultAsync(p => p.Id == produtoId);
                if (produto == null)
                    return new RespostaVenda { Sucesso = false, Mensagem = "Produto não encontrado" };

                // Verifica se é um iPhone (para aplicar promoção)
                bool isIphone = produto.Name.Contains("IPhone", StringComparison.OrdinalIgnoreCase);

                if (isIphone)
                {
                    // Busca a promoção ativa
                    var promocao = await _context.Promocoes.FirstOrDefaultAsync();
                    if (promocao == null)
                        throw new InvalidOperationException("Promoção não encontrada");

                    var horaAtual = DateTime.UtcNow.Hour;

                    // Se a hora mudou, reinicia a quantidade disponível para a nova hora
                    if (promocao.Hora != horaAtual)
                    {
                        promocao.Hora = horaAtual;
                        promocao.UnidadesDisponiveis = 100;
                        promocao.UltimaAtualizacao = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }

                    // Verifica se há unidades suficientes para a compra
                    if (promocao.UnidadesDisponiveis < quantidade)
                    {
                        return new RespostaVenda
                        {
                            Sucesso = false,
                            Mensagem = $"Unidades insuficientes. Disponível: {promocao.UnidadesDisponiveis}",
                            UnidadesDisponiveis = promocao.UnidadesDisponiveis
                        };
                    }

                    // Deduz a quantidade comprada da promoção
                    promocao.UnidadesDisponiveis -= quantidade;
                    promocao.UltimaAtualizacao = DateTime.UtcNow;

                    // Registra a venda no banco
                    _context.Vendas.Add(new Venda
                    {
                        Quantidade = quantidade,
                        Hora = promocao.Hora,
                        DataVenda = DateTime.UtcNow
                    });
                }
                else
                {
                    // Para outros produtos, apenas reduz o estoque normalmente
                    produto.StockQuantity -= quantidade;
                }

                // Salva todas as alterações no banco
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Retorna resposta de sucesso com informações da venda
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
                // 🔑 Libera o semáforo para que outra thread possa entrar
                _semaphore.Release();
            }
        }

        // Método para obter a disponibilidade atual de iPhones
        public async Task<int> ObterDisponibilidadeAtual()
        {
            var promocao = await _context.Promocoes.FirstOrDefaultAsync();
            if (promocao == null) return 0;

            // Se a hora mudou, reinicia a quantidade disponível
            var horaAtual = DateTime.UtcNow.Hour;
            if (promocao.Hora != horaAtual)
            {
                promocao.Hora = horaAtual;
                promocao.UnidadesDisponiveis = 100;
                promocao.UltimaAtualizacao = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            // Retorna a quantidade disponível
            return promocao.UnidadesDisponiveis;
        }
    }
}
