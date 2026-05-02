# HomeVault

[![CI](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml/badge.svg)](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml)

> A personal home-inventory web application built with ASP.NET Core 8 — track every item that matters, know what it's worth, and have the records ready before you ever need them.

HomeVault is a multi-tenant insurance catalog where each resident logs in to manage their own private list of household items. The project's focus is **secure-by-default data isolation**: every database query is scoped by the user's `CatalogId` so one resident can never see or modify another resident's items, even with a crafted request.

---

## Highlights

- **Multi-layered authentication** — cookie auth with sliding 30-minute expiration, HTTPS-only `Secure` cookies, `HttpOnly`, `SameSite=Lax`.
- **BCrypt password hashing** — plaintext passwords are never persisted and cannot be recovered.
- **Per-user data isolation** enforced at the query level, not just at the controller. Validated by automated security tests.
- **Brute-force protection** — login endpoint rate-limited to 5 attempts per IP per minute, with `Retry-After` hints.
- **Hardened response headers** — strict CSP, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy` on every response.
- **CSRF protection** via `[ValidateAntiForgeryToken]` on every state-changing action.
- **Login-first UX** — unauthenticated requests redirect to `/Account/Login`; successful login routes back to the originally requested URL.
- **EF Core migrations** for repeatable schema versioning.
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
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` (built-in, .NET 8) |
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

## Security defenses

Beyond the data-isolation guarantee tested above, HomeVault layers in standard web-app hardening:

| Defense | Where | What it does |
|---|---|---|
| **Brute-force login protection** | `Program.cs` rate limiter + `[EnableRateLimiting("login")]` | 5 attempts per IP per minute → `429 Too Many Requests` with `Retry-After: 60` |
| **BCrypt password hashing** | `AccountController.Signup` / `Login` | Salted, slow hash; resistant to rainbow tables and parallel cracking |
| **Strong password policy** | `SignupViewModel` | Minimum 8 characters, must contain at least one letter and one digit |
| **Secure cookies** | `Program.cs` cookie auth options | `HttpOnly`, `SameSite=Lax`, `SecurePolicy=Always` (HTTPS-only) |
| **CSRF protection** | `[ValidateAntiForgeryToken]` on every POST | Anti-forgery cookie also marked `Secure` |
| **HTTPS enforcement** | `app.UseHttpsRedirection()` + HSTS in production | Forces TLS, removes mixed-content downgrade risk |
| **Strict CSP** | `SecurityHeadersMiddleware` | `default-src 'self'`, no inline scripts, no framing — mitigates most XSS |
| **Clickjacking protection** | `X-Frame-Options: DENY` + CSP `frame-ancestors 'none'` | Page cannot be embedded in any iframe |
| **MIME-sniffing protection** | `X-Content-Type-Options: nosniff` | Browsers honor declared `Content-Type` |
| **Referrer minimization** | `Referrer-Policy: strict-origin-when-cross-origin` | Cross-origin requests leak only the origin, not the full URL |
| **Browser feature lockdown** | `Permissions-Policy` | Camera, microphone, geolocation explicitly disabled |
| **Open-redirect prevention** | `LocalRedirect()` for post-login `returnUrl` | External URLs are rejected by the framework |
| **Generic auth errors** | "Invalid username or password" | No username-enumeration via login responses |
| **Failed-login logging** | `_logger.LogWarning(...)` in `AccountController.Login` | Structured warning per failure with username + remote IP, ready for SIEM ingestion |
| **Parameterized queries** | All DB access via EF Core LINQ | No string-concatenated SQL anywhere — SQL injection impossible by construction |

---

## Getting started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server — [LocalDB](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb) on Windows is the simplest option

### Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/ychtsa/HomeVault.git
   cd HomeVault
   ```

2. Configure the connection string via [.NET user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) — never commit it to source:
   ```bash
   cd src/HomeVault
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\mssqllocaldb;Database=HomeVault;Trusted_Connection=True;MultipleActiveResultSets=true"
   ```

3. Run the app:
   ```bash
   dotnet run
   ```
   EF Core applies pending migrations automatically on startup.

4. Open https://localhost:5001 (or whichever port is reported on launch), click **Sign up**, and create your own account.

> The project intentionally ships with no pre-seeded users — the signup flow itself is part of the demo. Create an account, log in, add some items, and try the security guarantees by inspecting browser dev tools (response headers) or by trying to guess another user's item URL.

---

## Running the tests

```bash
dotnet test HomeVault.slnx
```

The test suite covers:

- **`BCryptTests`** — round-trip hashing, wrong-password rejection, salt uniqueness.
- **`SignupTests`** — duplicate-username rejection and atomic Catalog/Resident/User creation.
- **`CatalogIsolationTests`** — the security-critical cross-tenant access tests described above.

All tests run automatically in CI on every push and pull request to `main`.

---

## Project structure

```
HomeVault/
├── src/HomeVault/                  ASP.NET Core MVC application
│   ├── Controllers/                Account, Home, Items
│   ├── Data/                       EF Core DbContext
│   ├── Middleware/                 Custom middleware (security headers)
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
