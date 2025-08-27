using System.Reflection;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.Models;
using Shared.Models.AuthUser;
using Shared.Models.PromocaoVendas;

namespace Shared.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // StockService
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<StockHistory> StockHistories { get; set; } = null!;

        // SalesService
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;

        // Promoções/Vendas
        public DbSet<Promocao> Promocoes { get; set; } = null!;
        public DbSet<Venda> Vendas { get; set; } = null!;

        // Usuários
        public DbSet<UserModel> Users { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Opcional: suprimir warnings de pending changes temporariamente
            optionsBuilder.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            // Configurações globais de string e decimal
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var stringProperties = entityType.ClrType.GetProperties()
                    .Where(p => p.PropertyType == typeof(string));

                foreach (var prop in stringProperties)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(prop.Name)
                        .HasMaxLength(200);
                }

                var decimalProperties = entityType.ClrType.GetProperties()
                    .Where(p => (p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?)) &&
                                !Attribute.IsDefined(p, typeof(NotMappedAttribute)));

                foreach (var prop in decimalProperties)
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(prop.Name)
                        .HasPrecision(18, 2);
                }
            }

            ConfigureOrderModel(modelBuilder);
            ConfigureOrderItemModel(modelBuilder);
            ConfigureProductModel(modelBuilder);

            // Seed de Promoção (valores fixos para não quebrar migration)
            modelBuilder.Entity<Promocao>().HasData(
                new Promocao
                {
                    Id = 1,
                    Hora = 12,
                    UnidadesDisponiveis = 100,
                    UltimaAtualizacao = new DateTime(2025, 8, 26)
                }
            );
        }

        private static void ConfigureOrderModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrderDate)
                      .HasDefaultValueSql("CURRENT_TIMESTAMP") // SQLite
                      .ValueGeneratedOnAdd();
                entity.Property(e => e.TotalAmount)
                      .HasColumnType("decimal(18,2)");
            });
        }

        private static void ConfigureOrderItemModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UnitPrice)
                      .HasColumnType("decimal(18,2)");
                entity.Ignore(e => e.TotalPrice);
            });
        }

        private static void ConfigureProductModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Price)
                      .HasColumnType("decimal(18,2)");
                entity.Property(e => e.StockQuantity)
                      .HasDefaultValue(0);
            });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await ProcessStockChanges();
            return await base.SaveChangesAsync(cancellationToken);
        }

        private async Task ProcessStockChanges()
        {
            var entries = ChangeTracker.Entries<Product>()
                .Where(e => e.State == EntityState.Modified &&
                            e.Property(p => p.StockQuantity).IsModified);

            foreach (var entry in entries)
            {
                var originalQuantity = entry.OriginalValues.GetValue<int>(nameof(Product.StockQuantity));
                var currentQuantity = entry.CurrentValues.GetValue<int>(nameof(Product.StockQuantity));

                if (originalQuantity != currentQuantity)
                {
                    var history = new StockHistory(entry.Entity.Id, "system")
                    {
                        ProductId = entry.Entity.Id,
                        OldQuantity = originalQuantity,
                        NewQuantity = currentQuantity,
                        ChangedAt = DateTime.UtcNow
                    };

                    await StockHistories.AddAsync(history);
                }
            }
        }
    }
}
