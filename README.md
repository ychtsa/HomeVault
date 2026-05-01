# HomeVault

[![CI](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml/badge.svg)](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml)

> A personal home-inventory web application built with ASP.NET Core 8 — track every item that matters, know what it's worth, and have the records ready before you ever need them.

HomeVault is a multi-tenant insurance catalog where each resident logs in to manage their own private list of household items. The project's focus is **secure-by-default data isolation**: every database query is scoped by the user's `CatalogId` so one resident can never see or modify another resident's items, even with a crafted request.

---

## Highlights

- **Cookie-based authentication** with sliding 30-minute expiration and `HttpOnly` + `SameSite=Lax` cookies.
- **BCrypt password hashing** — plaintext passwords are never persisted and cannot be recovered.
- **Per-user data isolation** enforced at the query level (not just at the controller). Validated by automated security tests.
- **CSRF protection** via `[ValidateAntiForgeryToken]` on every state-changing action.
- **Login-first UX** — unauthenticated requests are redirected to `/Account/Login`; successful login routes back to the originally requested URL.
- **EF Core migrations** for repeatable schema versioning, plus an idempotent demo-data seeder.
- **CI on every push** — GitHub Actions restores, builds, and runs the full test suite on .NET 8.

---

## Tech stack

| Layer | Choice |
|---|---|
| Framework | ASP.NET Core 8 (MVC + Razor) |
| ORM | Entity Framework Core 8.0.10 |
| Database | SQL Server (LocalDB or full SQL Server) |
| Auth | Cookie authentication scheme |
| Hashing | BCrypt.Net-Next 4.1.0 |
| Tests | xUnit + Moq + EF Core InMemory |
| CI | GitHub Actions |
| UI | Bootstrap 5 |

---

## Security architecture

HomeVault treats catalog isolation as a **database-level concern**, not just a controller-level one.

1. On login, the user's `CatalogId` is written into the auth cookie as a claim.
2. Every read/write in `ItemsController` is filtered by `CurrentCatalogId` from that claim:
   ```csharp
   await _context.CatalogItems
       .FirstOrDefaultAsync(i => i.ItemId == id && i.CatalogId == CurrentCatalogId);
   ```
3. A request with a guessed `ItemId` belonging to another user resolves to `null` and the controller returns `NotFound()` — no information leaked, no cross-tenant access possible.

This claim is proven correct by `tests/HomeVault.Tests/CatalogIsolationTests.cs`, which seeds two users in an in-memory database and asserts that:
- `Index` returns only the signed-in user's items
- `Edit` for another user's item returns `404`
- `Delete` for another user's item returns `404`

These tests run on every push to `main`.

---

## Getting started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (LocalDB on Windows works out of the box, or any SQL Server instance)

### Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/ychtsa/HomeVault.git
   cd HomeVault
   ```

2. Configure the connection string via [.NET user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) — never commit it to the repo:
   ```bash
   cd src/HomeVault
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\mssqllocaldb;Database=HomeVault;Trusted_Connection=True;MultipleActiveResultSets=true"
   ```

3. Apply migrations and run:
   ```bash
   dotnet run
   ```

   The app applies pending migrations automatically and seeds two demo accounts on first startup.

4. Open https://localhost:5001 (or whichever port is reported on launch).

---

## Demo accounts

Two accounts are seeded on first run for evaluation:

| Username | Password | Items |
|---|---|---|
| `demo1` | `Demo123!` | Alice's catalog (4 items) |
| `demo2` | `Demo123!` | Bob's catalog (3 items) |

Sign in as `demo1` and try guessing one of Bob's item URLs (e.g. `/Items/Edit/i0005`) — you'll get a `404`, demonstrating the isolation guarantee.

---

## Running the tests

```bash
dotnet test HomeVault.slnx
```

The test suite covers:
- **`BCryptTests`** — round-trip hashing, wrong-password rejection, salt uniqueness.
- **`SignupTests`** — duplicate-username rejection and atomic Catalog/Resident/User creation.
- **`CatalogIsolationTests`** — the security-critical cross-tenant access tests described above.

---

## Project structure

```
HomeVault/
├── src/HomeVault/                  ASP.NET Core MVC application
│   ├── Controllers/                Account, Home, Items
│   ├── Data/                       EF Core DbContext + seeder
│   ├── Models/
│   │   ├── Entities/               Catalog, CatalogItem, Resident, ResidentUser
│   │   └── ViewModels/             Login, Signup, CatalogItemForm
│   ├── Views/                      Razor templates (Account, Home, Items, Shared)
│   ├── Migrations/                 EF Core migrations
│   ├── wwwroot/                    Static assets (CSS, JS, libs)
│   └── Program.cs                  Composition root
├── tests/HomeVault.Tests/          xUnit test project
├── .github/workflows/ci.yml        Build + test on push/PR to main
└── HomeVault.slnx                  Solution file
```

---

## License

Released under the [MIT License](LICENSE).
