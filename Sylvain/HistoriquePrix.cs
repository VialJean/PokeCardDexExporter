using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sylvain
{
    public record HistoriquePrix(long DateScan,Etat Etat,float Prix)
    {
        [Key]
        public int Id { get; set; }
        public Guid IdProduit { get; set; }
    }
}
