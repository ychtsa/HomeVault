# HomeVault

[![CI](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml/badge.svg)](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml)

> A personal home-inventory web application built with ASP.NET Core 8 — track every item that matters, know what it's worth, and have the records ready before you ever need them.

HomeVault is a multi-tenant insurance catalog where each resident logs in to manage their own private list of household items, attach photos, and download a printable PDF inventory ready to hand to an insurer. The project's focus is **secure-by-default data isolation**: every database query, every uploaded image, and every generated report is scoped by the user's `CatalogId` so one resident can never see or modify another resident's data, even with a crafted request.

---

## Highlights

- **Full account lifecycle** — sign up with email, log in, **password recovery** by email link with single-use tokens (SHA-256-hashed at rest, 60-min expiry), CSRF-protected logout.
- **Per-user data isolation** enforced at the query level for both rows and image bytes. Validated by automated security tests.
- **Photo upload** for every catalog item — stored outside `wwwroot`, served via authorized `/Items/Image/{id}` action that re-applies the same `CatalogId`-claim isolation as the rest of the API.
- **PDF insurance report** — one click on the catalog page produces a printable A4 inventory (resident details, items table, totals, page numbers) ready for an insurance claim.
- **Brute-force protection** — login rate-limited to 5 attempts per IP per minute, with `Retry-After` hints. Failed logins are logged with username + IP for SIEM ingestion.
- **Hardened response headers** — strict CSP, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy` on every response.
- **Structured logging** — Serilog console + rolling file sink (`logs/homevault-*.log`, 14-day retention) with one structured line per HTTP request.
- **Health endpoint** — `/health` pings the DB via the EF Core health check, ready to wire to a load balancer or uptime monitor.
- **Resilient data layer** — `EnableRetryOnFailure()` on the DbContext absorbs transient SQL outages with exponential backoff.
- **End-to-end tested** — 14 tests covering BCrypt hashing, signup, cross-tenant isolation, security headers, anti-forgery, the full HTTP pipeline, and the rate limiter actually returning 429.

---

## Tech stack

| Layer | Choice |
|---|---|
| Framework | ASP.NET Core 8 (MVC + Razor) |
| ORM | Entity Framework Core 8.0.10 (SQL Server, with retry-on-failure) |
| Database | SQL Server (LocalDB or full SQL Server) |
| Auth | Cookie authentication scheme |
| Password hashing | BCrypt.Net-Next 4.1.0 |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` (built-in, .NET 8) |
| PDF generation | QuestPDF 2024.7 (Community license) |
| Logging | Serilog (console + rolling file sink) |
| Health checks | `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` |
| Tests | xUnit + Moq + EF Core InMemory + `WebApplicationFactory<Program>` |
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

The same guarantee applies to **uploaded photos**: image bytes live at `App_Data/uploads/{CatalogId}/{ItemId}` (outside `wwwroot`) and are served only by an authorized `/Items/Image/{id}` action that re-runs the `CatalogId` filter. There is no static URL for any photo.

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
| **BCrypt password hashing** | `AccountController.Signup` / `Login` / `ResetPassword` | Salted, slow hash; resistant to rainbow tables and parallel cracking |
| **Strong password policy** | `SignupViewModel`, `ResetPasswordViewModel` | Minimum 8 characters, must contain at least one letter and one digit |
| **Hashed reset tokens** | `AccountController.ForgotPassword` | 32-byte URL-safe tokens; only their SHA-256 hash is persisted, with 60-minute expiry; cleared on use |
| **Email-enumeration prevention** | `ForgotPassword` / `Login` | Same confirmation page is shown whether or not the email exists; same generic error on bad credentials |
| **Secure cookies** | `Program.cs` cookie auth options | `HttpOnly`, `SameSite=Lax`, `SecurePolicy=Always` (HTTPS-only) |
| **CSRF protection** | `[ValidateAntiForgeryToken]` on every POST | Anti-forgery cookie also marked `Secure` |
| **HTTPS enforcement** | `app.UseHttpsRedirection()` + HSTS in production | Forces TLS, removes mixed-content downgrade risk |
| **Strict CSP** | `SecurityHeadersMiddleware` | `default-src 'self'`, no inline scripts, no framing — mitigates most XSS |
| **Clickjacking protection** | `X-Frame-Options: DENY` + CSP `frame-ancestors 'none'` | Page cannot be embedded in any iframe |
| **MIME-sniffing protection** | `X-Content-Type-Options: nosniff` | Browsers honor declared `Content-Type` |
| **Referrer minimization** | `Referrer-Policy: strict-origin-when-cross-origin` | Cross-origin requests leak only the origin, not the full URL |
| **Browser feature lockdown** | `Permissions-Policy` | Camera, microphone, geolocation explicitly disabled |
| **Image-byte isolation** | `/Items/Image/{id}` authorized action | Photos live outside `wwwroot`; served only after the same `CatalogId` filter applied to other reads |
| **Upload validation** | `ItemsController.ValidateUploadedImage` | 5 MB cap; allow-listed content types (`image/jpeg`, `image/png`, `image/webp`); path-traversal-safe filenames |
| **Open-redirect prevention** | `LocalRedirect()` for post-login `returnUrl` | External URLs are rejected by the framework |
| **Failed-login logging** | `_logger.LogWarning(...)` in `AccountController.Login` | Structured warning per failure with username + remote IP |
| **Parameterized queries** | All DB access via EF Core LINQ | No string-concatenated SQL anywhere — SQL injection impossible by construction |

---

## Operational readiness

| Concern | Implementation |
|---|---|
| Structured logging | Serilog with console + rolling file sink (`logs/homevault-{YYYYMMDD}.log`, 14-day retention). `UseSerilogRequestLogging` writes one line per HTTP request with method, path, status, and duration. |
| Health check | `GET /health` pings the database via the EF Core health check. Returns 200 OK / 503 with the failing component, ready for load balancers and uptime monitors. |
| Transient SQL resilience | `EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: 5s)` on the DbContext absorbs network blips, failovers, and SQL throttling. |
| Outbound email | Routed through `IEmailSender`. The bundled `LogEmailSender` writes to the app log so the password-reset flow runs end-to-end without an SMTP server. Production swaps in an SMTP / SendGrid implementation behind the same interface. |
| Image storage | `ICatalogImageStorage` abstraction. The default `FilesystemCatalogImageStorage` writes to `App_Data/uploads/`; cloud deployments can substitute Azure Blob Storage / S3 without touching controllers. |
| PDF generation | `IInsuranceReportGenerator` (QuestPDF Community license) renders an A4 report with header, items table, totals, and page numbers. Wrapping it in an interface keeps the controller library-agnostic. |

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

### Suggested demo path (for evaluators)

1. **Sign up** — create an account with any email; the password rules require ≥ 8 chars + a letter + a digit.
2. **Add an item** with a photo upload (JPEG / PNG / WebP, ≤ 5 MB).
3. **Download Report** on the catalog page — produces a PDF.
4. **Open browser dev tools** on any page and check the response headers — every response carries the strict CSP, `X-Frame-Options: DENY`, etc.
5. **Try guessing another user's URL** — e.g., visit `/Items/Edit/abcde` for an `ItemId` you don't own. Returns `404`, not the item.
6. **Forgot password flow** — log out, click "Forgot your password?", enter the email you used at signup. Look in the **app log** (console or `logs/homevault-{date}.log`) for a line starting with `[EMAIL]` containing the reset link. Open the link, choose a new password, log in.

---

## Running the tests

```bash
dotnet test HomeVault.slnx
```

The 14-test suite covers:

- **`BCryptTests`** — round-trip hashing, wrong-password rejection, salt uniqueness.
- **`SignupTests`** — duplicate-username rejection and atomic Catalog/Resident/User creation.
- **`CatalogIsolationTests`** — cross-tenant access on `Index`, `Edit`, and `Delete` returns 404.
- **`IntegrationTests`** — full HTTP pipeline: anonymous root redirect, `/health`, security headers stamped on every response, anti-forgery enforcement, anti-forgery token rendered into the form HTML.
- **`RateLimitingTests`** — `POST /Account/Login` returns `429 Too Many Requests` with `Retry-After` after the configured threshold.

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
│   │   └── ViewModels/             Login, Signup, ForgotPassword, ResetPassword,
│   │                               CatalogItemForm
│   ├── Services/                   IEmailSender / LogEmailSender,
│   │                               ICatalogImageStorage / FilesystemCatalogImageStorage,
│   │                               IInsuranceReportGenerator / QuestPdfInsuranceReportGenerator
│   ├── Views/                      Razor templates (Account, Home, Items, Shared)
│   ├── Migrations/                 EF Core migrations
│   ├── App_Data/uploads/           Per-catalog photo storage (gitignored)
│   ├── wwwroot/                    Static assets (CSS, JS, libs)
│   ├── logs/                       Serilog rolling-file output (gitignored)
│   └── Program.cs                  Composition root
├── tests/HomeVault.Tests/          xUnit test project
│   ├── BCryptTests.cs
│   ├── SignupTests.cs
│   ├── CatalogIsolationTests.cs
│   ├── IntegrationTests.cs
│   ├── RateLimitingTests.cs
│   └── HomeVaultWebAppFactory.cs   Test host with InMemory DB
├── .github/workflows/ci.yml        Build + test on push/PR to main
└── HomeVault.slnx                  Solution file
```

---

## License

Released under the [MIT License](LICENSE).
