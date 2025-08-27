using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shared.Models.PromocaoVendas
{
    public class Venda
    {
         public int Id { get; set; }
        public int Quantidade { get; set; }
        public int Hora { get; set; }
        public DateTime DataVenda { get; set; }
    }
}