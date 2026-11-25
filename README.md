# HomeVault

[![CI](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml/badge.svg)](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml)

A private home inventory app for tracking belongings and their value, storing photos, and exporting PDF reports for insurance. Each user's inventory, photos, and reports are isolated by the authenticated user's `CatalogId`.

## Features and Architecture

| Component | Implementation |
| --- | --- |
| Web API | ASP.NET Core 8, `[ApiController]`, JSON / Problem Details, Swagger UI |
| Database | EF Core 8 + SQL Server; Azure uses the SQL free tier and managed identity |
| Frontend | React + TypeScript + Vite, with a responsive Traditional Chinese interface at `/app/` |
| Photos | The free deployment uses persistent storage at `/home/uploads`; an optional private Azure Blob implementation is also available |
| Containers | Multi-stage Docker build with bundled React static assets, running as a non-root user |
| CI/CD | Build and test on PRs and main; after main passes, deploy a SHA-tagged image from public GHCR using GitHub OIDC |
| Azure | Bicep restricts deployment to Linux F1 and pauses SQL when the free allowance is exhausted; no ACR or Blob resources are created |

**A deployable configuration is provided to meet the requirement of zero Azure charges. No Azure resources have been provisioned, and the app has not been deployed live.** Before deployment, configure your own Azure subscription, GitHub environment, and SQL users. See the [Azure deployment guide](docs/azure-deployment.md).

React and the existing MVC app share the same ASP.NET Core host, database, and authentication cookie. Existing Razor pages remain available; the new frontend is at `/app/`, and the API is at `/api/`. This same-origin architecture requires no cross-origin CORS configuration and does not store authentication credentials in localStorage.

## Local Development

Requires the .NET 8 SDK, Node.js 22, and SQL Server. Windows users can use LocalDB. The SQL Server Linux Docker image requires x86-64; see the deployment guide for Apple Silicon limitations.

1. Configure the database connection using user-secrets. Do not commit passwords to Git:

   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "YOUR_SQL_SERVER_CONNECTION_STRING" --project src/HomeVault
   ```

2. Build the frontend and start the HTTPS backend:

   ```bash
   npm ci --prefix src/HomeVault.Client
   npm run build --prefix src/HomeVault.Client
   dotnet dev-certs https --trust
   dotnet run --project src/HomeVault --launch-profile https
   ```

3. Open **https://localhost:7031/app/**. Swagger is available at **https://localhost:7031/swagger/**.

EF migrations are applied automatically in Development by default. Production does not run DDL by default; apply migrations beforehand using a separate deployment identity:

```bash
dotnet run --project src/HomeVault --no-launch-profile -- --migrate-only
```

This command exits after applying migrations and returns a nonzero exit code on failure. Supply the connection through user-secrets or the `ConnectionStrings__DefaultConnection` environment variable.

When working on React, you can also run `npm run dev --prefix src/HomeVault.Client`. Vite proxies `/api` to `https://localhost:7031`, so the backend must be running too. Before publishing, run `npm run build` in the frontend project to generate output in `src/HomeVault/wwwroot/app`. The Dockerfile handles this step automatically; build React first when running `dotnet publish` on its own.

## Docker

```bash
cp .env.example .env
# Edit .env and set your own MSSQL_SA_PASSWORD
docker compose up --build -d
```

Open **https://localhost:8443/app/**. See the [deployment guide](docs/azure-deployment.md) for instructions on trusting the local Caddy certificate. SQL data, photos, and cookie encryption keys use persistent volumes. `docker compose down` preserves the data; `docker compose down -v` deletes it.

## API

Development exposes `/swagger/` and `/swagger/v1/swagger.json` by default. Set `Swagger__Enabled=true` to enable them in other environments. Swagger UI automatically obtains a CSRF token for the current identity when making write requests.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/auth/csrf` | Get `{ token }` and an antiforgery cookie |
| POST | `/api/auth/signup` | Register an account; log in after registration succeeds |
| POST | `/api/auth/login` | Log in and set an HttpOnly cookie |
| GET | `/api/auth/me` | Get the current user |
| POST | `/api/auth/logout` | Log out |
| GET / POST | `/api/items` | List / create items |
| GET / PUT / DELETE | `/api/items/{id}` | Read / update / delete an item |
| GET / POST / DELETE | `/api/items/{id}/image` | Read / upload / delete a photo |
| GET | `/api/items/report` | Export a PDF for the authenticated user |

Item request format:

```json
{ "itemName": "Camera", "itemType": "Electronics", "itemValue": 25000 }
```

Names and categories are limited to 30 characters, and values must be positive integers. Responses also include `itemId` and `imageUrl`; client-supplied `CatalogId` values are not accepted. Upload photos through the `file` field using `multipart/form-data`. The maximum size is 5 MiB, with JPEG, PNG, and WebP supported. The API validates file headers.

Before any POST, PUT, or DELETE request, obtain a CSRF token and send it in the `X-CSRF-TOKEN` header along with the cookies. Obtain a new token after logging in or out. Unauthenticated API requests return `401` without redirecting to HTML; requests for another user's items return `404`.

## Configuration

For ASP.NET Core configuration in environment variables, replace `:` with `__`:

| Setting | Default / Purpose |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | Required SQL Server / Azure SQL connection string |
| `Storage:Provider` | `FileSystem` or `AzureBlob` |
| `Storage:BlobServiceUri` | Azure Blob endpoint, authenticated with `DefaultAzureCredential` |
| `Storage:FileSystemRoot` | `/home/uploads` for the free Azure deployment, separate from the container filesystem |
| `Storage:ContainerName` | Private container name for the optional Azure Blob implementation; defaults to `catalog-images` |
| `Storage:ConnectionString` | Optional local Blob connection string; production uses managed identity |
| `AZURE_CLIENT_ID` | Azure runtime managed identity client ID |
| `DataProtection:KeysPath` | Persistent path for cookie encryption keys; `/home/keys` on Azure |
| `Database:ApplyMigrations` | When unspecified, migrations run automatically only in Development |
| `Swagger:Enabled` | When unspecified, Swagger is enabled only in Development |
| `ForwardedHeaders:Enabled` | Defaults to `false`; enable when running behind a reverse proxy |
| `ForwardedHeaders:TrustAllProxies` | Use only in controlled reverse proxy environments where the container cannot be accessed directly |
| `ForwardedHeaders:KnownProxies` | Custom allowlist of reverse proxy IP addresses |

`/health` checks database connectivity for App Service and Docker. App Service terminates HTTPS on Azure; health probes can use internal HTTP.

## Security and Operational Notes

- Uses BCrypt, cookies with a 30-minute sliding expiration, HTTPS, CSRF protection, login rate limiting, and account lockout. Private API responses use `Cache-Control: no-store`.
- The free deployment stores photos in App Service's persistent `/home/uploads` directory, accessible only through authorized endpoints. F1's 1 GB of file storage is shared by photos and cookie encryption keys. Clean up files when storage is full; the storage plan will not be upgraded.
- Persist the Data Protection keys used for cookies. Azure mounts `/home`, and local Docker mounts `App_Data`. Do not delete the key volume.
- The Azure SQL runtime identity has only data read/write permissions; migrations use a separate deployment identity. See the deployment guide for initial SQL user setup.
- The existing password recovery flow uses `LogEmailSender` and does not send real email. Connect an email service before offering password resets in production. During development, reset links are available in the logs.
- The free deployment does not create ACR, Blob, Log Analytics, or paid App Service plans. SQL uses `useFreeLimit=true` and `AutoPause` to pause when the allowance is exhausted. These settings are checked before deployment, which stops if validation fails.
- F1 and the SQL free allowance are suitable for demos: they have daily CPU, storage, and monthly compute quotas, may sleep, cold start, or pause, and provide no SLA. If free capacity is unavailable in a region, stop deployment instead of selecting a paid plan.

## Verification

```bash
dotnet test tests/HomeVault.Tests/HomeVault.Tests.csproj --configuration Release
npm test --prefix src/HomeVault.Client
npm run build --prefix src/HomeVault.Client
docker build -t homevault:local .
```

Backend tests cover the existing MVC app, the account lifecycle, API authentication and CSRF, tenant isolation for CRUD operations, photos, and reports, upload validation, Swagger documentation, and Blob error handling. HTTP integration tests use EF InMemory and do not replace deployment verification against SQL Server or Azure SQL.

CI uses commands targeting `.csproj` files for compatibility with the .NET 8 SDK. The existing `HomeVault.slnx` can be opened with a newer SDK that supports that format.

## Project Structure

```text
src/HomeVault/                 ASP.NET Core MVC + Web API
  Controllers/Api/             JSON API controllers
  Models/Api/                  API request and response DTOs
  Services/                    File / Azure Blob storage, PDF, email
  OpenApi/                     CSP-compliant Swagger UI page
  wwwroot/app/                 React build output (excluded from Git)
src/HomeVault.Client/          React + TypeScript + Vite
tests/HomeVault.Tests/         xUnit tests
infra/                        Azure Bicep, local HTTPS proxy
scripts/                      Deployment / initialization scripts
docs/azure-deployment.md       Azure deployment instructions
.github/workflows/             CI and deployment pipelines
Dockerfile / compose.yaml     Container build and local integration environment
```

## License

[MIT](LICENSE)
