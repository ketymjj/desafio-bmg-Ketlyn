using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shared.Models.PromocaoVendas
{
    public class Promocao
    {
        public int Id { get; set; }
        public int Hora { get; set; }
        public int UnidadesDisponiveis { get; set; }
        public DateTime UltimaAtualizacao { get; set; }
    }
}