using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shared.Models.PromocaoVendas
{
    public class RespostaVenda
    {
         public bool Sucesso { get; set; }
        public string Mensagem { get; set; } = string.Empty;
        public int UnidadesVendidas { get; set; }
        public int UnidadesDisponiveis { get; set; }
    }
}