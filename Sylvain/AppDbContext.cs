using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sylvain
{
    public class AppDbContext : DbContext
    {
        public DbSet<Produit> Products { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Chemin de la base de données SQLite
            optionsBuilder.UseSqlite("Data Source=products.db");
        }
    }
}
