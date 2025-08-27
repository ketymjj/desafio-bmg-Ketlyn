
using Shared.Models.PromocaoVendas;

namespace Shared.Interface
{
    public interface IPromocaoService
    {
        Task<RespostaVenda> ComprariPhone(int produtoId, int quantidade);
        Task<int> ObterDisponibilidadeAtual();
    }
}