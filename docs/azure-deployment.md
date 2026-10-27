# Azure 免費部署

本專案目前只準備程式與部署設定，尚未建立任何 Azure 資源。以下操作需在日後決定部署、完成自己的 Azure 登入後才執行。

## 零 Azure 費用的配置

| 資源 | 固定配置 |
| --- | --- |
| App Service | Linux **F1 Free**，一個 instance，關閉 Always On |
| Azure SQL | **Free offer**，`useFreeLimit=true`、`freeLimitExhaustionBehavior=AutoPause` |
| Docker registry | 公開 GHCR package；不建立 Azure Container Registry |
| 照片及登入金鑰 | App Service 隨附的持久化 `/home/uploads` 與 `/home/keys` |
| Azure Storage／監控 | 不建立 Blob、Files、Log Analytics、Application Insights |
| 存取權限 | managed identities 與 GitHub OIDC；不用 Azure 密碼或 publish profile |

F1 有每天 60 CPU 分鐘、1 GB RAM、1 GB 檔案空間等限制，適合學習與展示。SQL 免費方案每月提供 100,000 vCore-seconds、32 GB 資料及 32 GB 備份，超過免費額度後暫停至下個月，**不得改成 BillOverUsage**。休眠與冷啟動會影響首次回應；此配置沒有 SLA。

範本只允許 F1，沒有付費 fallback。若訂閱、區域或目前容量不支援免費方案，停止部署。首次建立前，在 Portal／what-if 確認 F1 與 SQL 免費方案的預估金額為 0；既有訂閱的其他資源不受本範本管理。預算警示不會自動阻止扣款，不能取代固定免費方案與 SQL AutoPause。

官方限制：

- [App Service 方案與 Linux 容器支援](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits#azure-app-service-limits)
- [Azure SQL 免費方案與 AutoPause](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql)
- [App Service 持久化檔案系統](https://github.com/projectkudu/kudu/wiki/Understanding-the-Azure-App-Service-file-system)
- [公開 GitHub Packages 的免費儲存](https://docs.github.com/en/billing/managing-billing-for-your-products/managing-billing-for-github-packages/about-billing-for-github-packages)

## 1. 先準備公開 Docker 映像

部署 workflow 只在公開 GitHub repository 執行，使用免費的標準 GitHub-hosted runner。公開映像不可包含連線字串、user-secrets 或照片；`.dockerignore` 已排除這些資料。

1. 將變更提交到自己 repository 的 `main`。
2. 建立 GitHub environment **production**，deployment branches 限制 `main`。Actions 設定需允許 workflow 寫入 Packages。
3. 在 Actions 手動執行 **Deploy free Azure**，勾選 **publish_only**。只會產生 `ghcr.io/<owner>/<repo>:<完整 commit SHA>`，不登入或修改 Azure。
4. 在 GitHub Packages 頁面將 container package Visibility 設為 **Public**。首次 package 通常預設 private；設定前確認自己的 GitHub Packages 額度／spending limit。後續 workflow 會用匿名 pull 檢查。
5. 記下映像完整名稱。Package 應連結此 repository，並授權 Actions 存取。

私人 repository、私人 registry、加大 runner 或既有付費 GitHub 用量可能產生費用，不在本免費配置內。

## 2. 預覽並建立免費 Azure 資源

需要 Azure CLI + Bicep，以及可建立 resource group、指派 RBAC/custom role 的權限。指定自己的 Microsoft Entra 使用者或群組作 SQL administrator，負責首次建立 SQL users。

```bash
az login
az account set --subscription YOUR_SUBSCRIPTION_ID
az appservice list-locations --sku F1 --linux-workers-enabled --output table
az group create --name homevault-free --location YOUR_SUPPORTED_REGION
cp infra/main.parameters.example.json /private/tmp/homevault.parameters.json
```

編輯暫存參數檔：

- `githubOidcSubject`：`repo:<owner>/<repo>:environment:production`，大小寫須與 GitHub 一致；若組織有自訂 OIDC subject，使用實際 subject。
- `sqlAdminObjectId`、`sqlAdminName`、`sqlAdminPrincipalType`：自己的 Entra SQL administrator。
- `containerImage`：上一節公開 GHCR 映像與完整 SHA。
- 保留 `appServiceSku=F1`；SQL Free/AutoPause 已在範本固定。

先預覽建立的資源：

```bash
az deployment group what-if --resource-group homevault-free --template-file infra/main.bicep --parameters @/private/tmp/homevault.parameters.json
```

確認只有免費方案、identities 與 RBAC，沒有 Storage/ACR/付費 plan，再執行：

```bash
az deployment group create --name homevault --resource-group homevault-free --template-file infra/main.bicep --parameters @/private/tmp/homevault.parameters.json
az deployment group show --name homevault --resource-group homevault-free --query properties.outputs --output json
```

若 Azure 拒絕免費方案，停止，不要調整 SKU 繞過。SQL 使用 Entra-only authentication，不建立 SQL administrator 密碼。

## 3. 初始化 SQL 使用者

從 deployment outputs 取得 `runtimePrincipalId` 與 `deployPrincipalId`。以 Entra SQL administrator 連入 **HomeVault database**，將 `infra/bootstrap-users.sql` 兩個全零 ID 改成這兩個 object IDs，再執行。

可使用 Portal SQL Query editor、SSMS 或 VS Code MSSQL。僅為自己的當前 IP 暫時開放 SQL firewall，完成後移除。腳本使用明確 SID，不需授予 SQL server Microsoft Graph Directory Readers。

- `homevault-runtime`：`db_datareader`、`db_datawriter`，負責一般 API。
- `homevault-deploy`：另加 `db_ddladmin`，只供 CD 套用 migrations。

Production 不會自動執行 migrations。SQL users 未建立時部署會失敗，不會改用高權限 runtime identity。

## 4. GitHub 自動部署

將 output `githubVariables` 每個欄位加到 GitHub **production environment variables**：

| Variable | 來源 |
| --- | --- |
| `AZURE_CLIENT_ID` | GitHub deploy identity 的 client ID |
| `AZURE_TENANT_ID` | Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| `AZURE_RESOURCE_GROUP` | `homevault-free` |
| `AZURE_WEBAPP_NAME` | 產生的 App Service 名稱 |
| `AZURE_SQL_SERVER` | SQL server 名稱，不含 `.database.windows.net` |
| `AZURE_SQL_DATABASE` | `HomeVault` |

最後在 **repository variables** 設 `AZURE_DEPLOY_ENABLED=true`。沒設定時，push 不會部署 Azure。

每次 main push 的 CI 成功後：

1. Checkout 通過 CI 的同一個 commit，建置 `linux/amd64` image，推送完整 SHA tag 至 GHCR。
2. 確認可匿名 pull，再使用 OIDC 登入 Azure。
3. `scripts/check-free-azure.sh` 確認 F1 與 SQL Free + AutoPause。
4. 僅開放 App Service 可能的 egress IP，以及當次 runner 的暫時 firewall rule。
5. 獨立 deploy identity 執行 `--migrate-only`；失敗就不推出新映像。
6. 更新 image、重啟，檢查 `/health` 與 `/app/`。
7. 無論成功失敗，都清除暫時 runner firewall rule。

Workflow 沒有建立資源或提高 SKU 的步驟。首次 RBAC 可能需要數分鐘傳播；稍後重跑即可。

## 5. 上線驗證與限制

- 開啟 `https://<app>.azurewebsites.net/app/`，註冊、登入、建立物品、上傳照片、匯出 PDF。
- 重啟 App Service，確認持久化照片與登入金鑰仍在。兩個帳號的清單及照片應隔離。
- SQL 額度用完應暫停，不額外計費。勿設定 keep-alive 或定時探測持續喚醒 SQL；`/health` 供手動／部署時驗證。
- 1 GB 持久化空間包含照片、金鑰及其他檔案；需自行備份。範本不建立額外的儲存或備份服務。
- 舊 SHA image 可回復程式；migration 不自動回滾，schema 變更須保持與前一版相容。
- Swagger 正式環境預設關閉。`LogEmailSender` 只寫日誌，真正寄送重設密碼信需另外設定。
- 不要把試用點數或預算警示當成永久免費方案。既有訂閱的其他計費不受此專案控制。

## 本機 Docker

```bash
cp .env.example .env
# 編輯 .env 設定自己的強密碼，勿提交此檔案
docker compose up --build -d
```

開啟 `https://localhost:8443/app/`。Caddy 使用本機 CA，第一次需信任自己的根憑證，或在開發瀏覽器確認本機憑證。可執行 `docker compose cp https:/data/caddy/pki/authorities/local/root.crt /private/tmp/homevault-local-ca.crt` 取得根憑證；不要提交私鑰。

官方 SQL Server Linux image 需要 x86-64，Compose 指定 `linux/amd64`。Apple Silicon 需 Docker Desktop 支援的模擬環境，Microsoft 不提供此架構的 SQL Server 正式支援。無法啟動時使用可用的遠端 SQL Server。

`sql-data` 保存資料庫，`app-data` 保存照片與金鑰。`docker compose down` 保留 volumes；`down -v` 永久刪除資料。本機操作不使用 Azure。
