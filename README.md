# HomeVault

[![CI](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml/badge.svg)](https://github.com/ychtsa/HomeVault/actions/workflows/ci.yml)

私人家庭物品清單：記錄物品與價值、保存照片，並匯出保險用 PDF。每位使用者的清單、照片與報表皆依登入身分的 `CatalogId` 隔離。

## 功能與架構

| 部分 | 實作 |
| --- | --- |
| Web API | ASP.NET Core 8、`[ApiController]`、JSON／Problem Details、Swagger UI |
| 資料庫 | EF Core 8 + SQL Server；Azure 使用 SQL 免費方案與受控識別 |
| 前端 | React + TypeScript + Vite，繁體中文響應式介面，入口 `/app/` |
| 照片 | 免費部署使用持久化 `/home/uploads`；亦提供可選的私人 Azure Blob 實作 |
| 容器 | 多階段 Docker build，整合 React 靜態檔案，非 root 執行 |
| CI/CD | PR／main 建置測試；main 通過後以 GitHub OIDC 部署公開 GHCR 的 SHA 標記映像 |
| Azure | Bicep 限定 Linux F1、SQL 免費額度用完即暫停，不建立 ACR 或 Blob |

**依零 Azure 費用要求提供可部署版本，尚未建立 Azure 資源或實際上線。** 部署前須設定自己的 Azure 訂閱、GitHub environment 與 SQL 使用者，詳見 [Azure 部署指南](docs/azure-deployment.md)。

React 和原有 MVC 共用同一個 ASP.NET Core host、資料庫及登入 cookie。既有 Razor 頁面仍可使用；`/app/` 為新前端，API 入口為 `/api/`。同來源架構不需開放跨站 CORS，也不把登入憑證存進 localStorage。

## 本機開發

需要 .NET 8 SDK、Node.js 22、SQL Server。Windows 可使用 LocalDB；Docker 的 SQL Server Linux 映像需要 x86-64，Apple Silicon 限制見部署指南。

1. 設定資料庫連線，使用 user-secrets，勿提交密碼至 Git：

   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "YOUR_SQL_SERVER_CONNECTION_STRING" --project src/HomeVault
   ```

2. 建置前端並啟動 HTTPS 後端：

   ```bash
   npm ci --prefix src/HomeVault.Client
   npm run build --prefix src/HomeVault.Client
   dotnet dev-certs https --trust
   dotnet run --project src/HomeVault --launch-profile https
   ```

3. 開啟 **https://localhost:7031/app/**；Swagger 為 **https://localhost:7031/swagger/**。

Development 預設自動套用 EF migrations。Production 預設不會執行 DDL，使用獨立部署身分事先套用：

```bash
dotnet run --project src/HomeVault --no-launch-profile -- --migrate-only
```

此指令套用 migrations 後結束，失敗時回傳非零 exit code。連線仍由 user-secrets 或 `ConnectionStrings__DefaultConnection` 環境變數提供。

修改 React 時，可另外執行 `npm run dev --prefix src/HomeVault.Client`。Vite 將 `/api` 代理至 `https://localhost:7031`，後端需同時執行。正式發佈前執行 `npm run build`，輸出至 `src/HomeVault/wwwroot/app`。Dockerfile 會自動完成這一步；單獨 `dotnet publish` 前也須先建置 React。

## Docker

```bash
cp .env.example .env
# 編輯 .env，設定自己的 MSSQL_SA_PASSWORD
docker compose up --build -d
```

使用 **https://localhost:8443/app/**；本機 Caddy 憑證的信任設定見 [部署指南](docs/azure-deployment.md)。SQL、照片與 cookie 金鑰皆使用持久化 volumes。`docker compose down` 會保留資料，`docker compose down -v` 會刪除資料。

## API

Development 預設提供 `/swagger/` 與 `/swagger/v1/swagger.json`；其他環境需設定 `Swagger__Enabled=true`。Swagger UI 的寫入請求會自動取得當前身分的 CSRF token。

| 方法 | 路徑 | 用途 |
| --- | --- | --- |
| GET | `/api/auth/csrf` | 取得 `{ token }` 與防偽 cookie |
| POST | `/api/auth/signup` | 註冊；成功後再登入 |
| POST | `/api/auth/login` | 登入，設定 HttpOnly cookie |
| GET | `/api/auth/me` | 目前使用者 |
| POST | `/api/auth/logout` | 登出 |
| GET / POST | `/api/items` | 清單／新增物品 |
| GET / PUT / DELETE | `/api/items/{id}` | 讀取／修改／刪除物品 |
| GET / POST / DELETE | `/api/items/{id}/image` | 讀取／上傳／刪除照片 |
| GET | `/api/items/report` | 匯出登入者的 PDF |

物品寫入格式：

```json
{ "itemName": "相機", "itemType": "電子產品", "itemValue": 25000 }
```

名稱與類別最多 30 字元，價值為正整數。回應另含 `itemId` 和 `imageUrl`；不接受客戶端指定 `CatalogId`。照片用 `multipart/form-data` 的 `file` 欄位上傳，最多 5 MiB，接受 JPEG／PNG／WebP，API 會檢查檔案標頭。

所有 POST、PUT、DELETE 均需先取得 CSRF token，再以 `X-CSRF-TOKEN` header 送出並帶上 cookie；登入／登出後須重新取得 token。未登入的 API 回傳 `401`，不導向 HTML；其他使用者的物品回傳 `404`。

## 設定

ASP.NET Core 設定在環境變數中以 `__` 代替 `:`：

| 設定 | 預設／用途 |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server／Azure SQL 連線字串，必填 |
| `Storage:Provider` | `FileSystem` 或 `AzureBlob` |
| `Storage:BlobServiceUri` | Azure Blob endpoint，使用 `DefaultAzureCredential` |
| `Storage:FileSystemRoot` | Azure 免費部署為 `/home/uploads`，與容器檔案系統分開 |
| `Storage:ContainerName` | 可選 Azure Blob 實作的私人 container 名稱，預設 `catalog-images` |
| `Storage:ConnectionString` | 可選的本機 Blob 連線字串，正式環境使用受控識別 |
| `AZURE_CLIENT_ID` | Azure runtime managed identity client ID |
| `DataProtection:KeysPath` | cookie 金鑰持久化路徑；Azure 為 `/home/keys` |
| `Database:ApplyMigrations` | 未指定時只在 Development 自動執行 |
| `Swagger:Enabled` | 未指定時只在 Development 啟用 |
| `ForwardedHeaders:Enabled` | 預設 `false`，在反向代理後方時啟用 |
| `ForwardedHeaders:TrustAllProxies` | 只用於無法直接連入容器的受控反向代理環境 |
| `ForwardedHeaders:KnownProxies` | 自訂反向代理 IP 白名單 |

`/health` 檢查資料庫連線，供 App Service 與 Docker 使用。Azure 的 HTTPS 由 App Service 終結；健康探測可使用內部 HTTP。

## 安全與操作注意事項

- 沿用 BCrypt、30 分鐘滑動 cookie、HTTPS、CSRF、登入速率限制與帳號鎖定。API 私人回應使用 `Cache-Control: no-store`。
- 免費部署把照片存於 App Service 的持久化 `/home/uploads`，只能由授權端點讀取。F1 的 1 GB 檔案空間同時容納照片與 cookie 金鑰；容量用完須清理，不會升級儲存方案。
- cookie 的 Data Protection 金鑰需持久化；Azure 掛載 `/home`，本機 Docker 掛載 `App_Data`。不要刪除金鑰 volume。
- Azure SQL runtime 身分只有資料讀寫權限，migration 使用獨立部署身分。首次 SQL 使用者初始化步驟見部署指南。
- 原有忘記密碼流程使用 `LogEmailSender`，不會寄出真正的信；提供正式密碼重設前需接上郵件服務。開發測試可在日誌取得重設連結。
- 免費部署不建立 ACR、Blob、Log Analytics 或付費 App Service plan。SQL 設定 `useFreeLimit=true` 與 `AutoPause`，額度用完即暫停；部署前會檢查這些設定，失敗時停止。
- F1 與 SQL 免費額度適合展示：有每日 CPU／儲存／每月計算配額，會休眠、冷啟動或暫停，沒有 SLA。若區域沒有免費容量，請停止部署，不要改選付費方案。

## 驗證

```bash
dotnet test tests/HomeVault.Tests/HomeVault.Tests.csproj --configuration Release
npm test --prefix src/HomeVault.Client
npm run build --prefix src/HomeVault.Client
docker build -t homevault:local .
```

後端測試涵蓋原有 MVC、帳號生命週期、API 認證／CSRF、跨租戶 CRUD／照片／報表隔離、上傳驗證、Swagger 文件與 Blob 錯誤處理。HTTP 整合測試使用 EF InMemory，不能取代 SQL Server／Azure SQL 部署驗證。

CI 使用 `.csproj` 指令以相容 .NET 8 SDK；既有 `HomeVault.slnx` 可由支援該格式的新版 SDK 開啟。

## 專案結構

```text
src/HomeVault/                 ASP.NET Core MVC + Web API
  Controllers/Api/             JSON API controllers
  Models/Api/                  API 請求及回應 DTO
  Services/                    檔案／Azure Blob、PDF、郵件
  OpenApi/                     符合 CSP 的 Swagger UI 頁面
  wwwroot/app/                 React 產出（不納入 Git）
src/HomeVault.Client/          React + TypeScript + Vite
tests/HomeVault.Tests/         xUnit 測試
infra/                        Azure Bicep、本機 HTTPS proxy
scripts/                      部署／初始化腳本
docs/azure-deployment.md       Azure 部署操作說明
.github/workflows/             CI 與部署 pipeline
Dockerfile / compose.yaml     容器建置與本機整合環境
```

## License

[MIT](LICENSE)
