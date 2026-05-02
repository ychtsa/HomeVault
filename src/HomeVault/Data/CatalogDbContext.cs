/*
 * FILE: CatalogDbContext.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: Entity Framework Core DbContext for the HomeVault database.
 *              Configures keys, relationships, and constraints via the
 *              Fluent API: 1-to-1 Resident<->Catalog, 1-to-1
 *              Resident<->ResidentUser, 1-to-many Catalog->CatalogItems,
 *              and a unique index on Username.
 */

using HomeVault.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeVault.Data
{
    public class CatalogDbContext : DbContext
    {
        /*
         * Function: CatalogDbContext(DbContextOptions<CatalogDbContext> options)
         * Description: Constructor invoked by ASP.NET Core's DI container.
         * Parameter: DbContextOptions<CatalogDbContext> options - configured
         *            options (provider, connection string).
         * Return: none (constructor).
         */
        public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
            : base(options)
        {
        }

        public DbSet<Catalog> Catalogs => Set<Catalog>();
        public DbSet<Resident> Residents => Set<Resident>();
        public DbSet<ResidentUser> ResidentUsers => Set<ResidentUser>();
        public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();

        /*
         * Function: OnModelCreating(ModelBuilder modelBuilder)
         * Description: Declares relationships and constraints that data
         *              annotations cannot express on their own.
         * Parameter: ModelBuilder modelBuilder - the EF Core model builder.
         * Return: void.
         */
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 1-to-1: Resident <-> Catalog
            modelBuilder.Entity<Resident>()
                .HasOne(r => r.Catalog)
                .WithOne(c => c.Resident)
                .HasForeignKey<Resident>(r => r.CatalogId);

            // 1-to-1: Resident <-> ResidentUser (sharing the same key)
            modelBuilder.Entity<ResidentUser>()
                .HasOne(u => u.Resident)
                .WithOne(r => r.User)
                .HasForeignKey<ResidentUser>(u => u.ResidentId);

            // 1-to-many: Catalog -> CatalogItems
            modelBuilder.Entity<CatalogItem>()
                .HasOne(i => i.Catalog)
                .WithMany(c => c.Items)
                .HasForeignKey(i => i.CatalogId);

            // Username must be unique across all users.
            modelBuilder.Entity<ResidentUser>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // Email must be unique across all users (enables password reset by email).
            modelBuilder.Entity<ResidentUser>()
                .HasIndex(u => u.Email)
                .IsUnique();

            base.OnModelCreating(modelBuilder);
        }
    }
}