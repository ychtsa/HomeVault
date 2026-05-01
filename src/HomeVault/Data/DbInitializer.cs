/*
 * FILE: DbInitializer.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: One-shot seeder that populates the database with two demo
 *              residents and a few sample catalog items the first time the
 *              app starts. Skips silently if data already exists, so it is
 *              safe to call on every startup.
 */

using HomeVault.Models.Entities;

namespace HomeVault.Data
{
    public static class DbInitializer
    {
        /*
         * Function: Seed(CatalogDbContext context)
         * Description: Inserts demo data only when the ResidentUsers table
         *              is empty. Two accounts are created (demo1 / demo2),
         *              each with its own catalog and four sample items.
         * Parameter: CatalogDbContext context - the EF Core context to seed.
         * Return: void.
         */
        public static void Seed(CatalogDbContext context)
        {
            // Idempotency: bail out if the database already has any users.
            if (context.ResidentUsers.Any())
            {
                return;
            }

            // ===== Demo user #1: Alice =====
            Catalog aliceCatalog = new Catalog { CatalogId = "cat01" };
            Resident alice = new Resident
            {
                ResidentId = "res01",
                ResidentName = "Alice Demo",
                ResidentAddress = "123 Maple Street, Waterloo, ON",
                CatalogId = "cat01"
            };
            ResidentUser aliceUser = new ResidentUser
            {
                ResidentId = "res01",
                Username = "demo1",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo123!")
            };

            List<CatalogItem> aliceItems = new List<CatalogItem>
            {
                new CatalogItem { ItemId = "i0001", CatalogId = "cat01",
                    ItemName = "MacBook Pro 14\"", ItemType = "Electronics", ItemValue = 2499 },
                new CatalogItem { ItemId = "i0002", CatalogId = "cat01",
                    ItemName = "Sony WH-1000XM5",  ItemType = "Electronics", ItemValue = 399 },
                new CatalogItem { ItemId = "i0003", CatalogId = "cat01",
                    ItemName = "Leather Sofa",     ItemType = "Furniture",   ItemValue = 1200 },
                new CatalogItem { ItemId = "i0004", CatalogId = "cat01",
                    ItemName = "Mountain Bike",    ItemType = "Recreation",  ItemValue = 850 }
            };

            // ===== Demo user #2: Bob =====
            Catalog bobCatalog = new Catalog { CatalogId = "cat02" };
            Resident bob = new Resident
            {
                ResidentId = "res02",
                ResidentName = "Bob Demo",
                ResidentAddress = "456 Oak Avenue, Kitchener, ON",
                CatalogId = "cat02"
            };
            ResidentUser bobUser = new ResidentUser
            {
                ResidentId = "res02",
                Username = "demo2",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo123!")
            };

            List<CatalogItem> bobItems = new List<CatalogItem>
            {
                new CatalogItem { ItemId = "i0005", CatalogId = "cat02",
                    ItemName = "iPhone 15 Pro",    ItemType = "Electronics", ItemValue = 1199 },
                new CatalogItem { ItemId = "i0006", CatalogId = "cat02",
                    ItemName = "Dyson V15",        ItemType = "Appliance",   ItemValue = 749 },
                new CatalogItem { ItemId = "i0007", CatalogId = "cat02",
                    ItemName = "Diamond Ring",     ItemType = "Jewelry",     ItemValue = 3500 }
            };

            context.Catalogs.AddRange(aliceCatalog, bobCatalog);
            context.Residents.AddRange(alice, bob);
            context.ResidentUsers.AddRange(aliceUser, bobUser);
            context.CatalogItems.AddRange(aliceItems);
            context.CatalogItems.AddRange(bobItems);
            context.SaveChanges();
        }
    }
}