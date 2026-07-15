# Azure Integration & Deployment Plan — WaterMe

## Context

The WaterMe project has completed all foundation phases (PRD, tech stack, infra research). Azure App Service + Azure SQL Database (free offer) was selected in `context/foundation/infrastructure.md`. The project is a .NET scaffold with no application code yet — this plan covers the Azure resource provisioning, the minimal code changes needed for Azure compatibility, and CI/CD wiring. Business logic (auth, plants, AI) is built separately on top of this deployed base.

---

## ⚠️ Critical: .NET Version Discrepancy

`water-me.csproj` targets **`net9.0`** and references `Microsoft.AspNetCore.OpenApi 9.0.11`.  
`infrastructure.md` was authored assuming `.NET 8 LTS`.

**Every Azure CLI command in this plan uses `DOTNETCORE:9.0` / `DOTNETCORE|9.0`**, not the `8.0` variants shown in `infrastructure.md`. The csproj stays as-is.

---

## Naming Tokens — Fill In Once, Use Everywhere

| Token | Your value | Notes |
|---|---|---|
| `<rg-name>` | | e.g. `rg-waterme-prod` |
| `<plan-name>` | | e.g. `plan-waterme-prod` |
| `<app-name>` | | globally unique on `.azurewebsites.net` |
| `<sql-server-name>` | | globally unique across all Azure |
| `<db-name>` | | e.g. `watermedb` |
| `<admin-user>` | | not `sa` or `admin` |
| `<strong-password>` | | store in password manager |
| `<storage-account-name>` | | 3–24 lowercase alphanumeric only, globally unique |
| `<blob-container-name>` | | e.g. `dataprotection-keys` (lowercase) |
| `<subscription-id>` | | 36-char UUID from `az account show` |

---

## Phase 0 — Pre-flight

**Prerequisite:** none — starting gate.

- [ ] **0.1** Verify Azure CLI version (minimum 2.57.0):
  ```
  az version
  ```
  If below minimum, install from `aka.ms/installazurecli`.

- [ ] **0.2** Log in to Azure:
  ```
  az login
  ```
  Confirm the correct account appears in the output JSON.

- [ ] **0.3** Set the target subscription as default and note `<subscription-id>`:
  ```
  az account show
  az account set --subscription "<subscription-id>"
  ```

- [ ] **0.4** Verify .NET SDK is version 9.x:
  ```
  dotnet --version
  ```
  Must return `9.x.x`. If `8.x.x`, install from `dotnet.microsoft.com/download/dotnet/9.0`.

- [ ] **0.5** Install GitHub CLI and authenticate (needed for secret registration in Phase 3):
  ```
  gh --version
  gh auth status
  ```
  If not installed: `winget install --id GitHub.cli`, then `gh auth login`.

- [ ] **0.6** Verify clean build before touching Azure:
  ```
  dotnet restore
  dotnet build --configuration Release
  ```
  Both must exit 0. Fix any build failures locally before continuing.

- [ ] **0.7** Confirm GitHub remote:
  ```
  git remote -v
  ```
  Expected: `https://github.com/BartiDev/WaterMe.git` (or SSH equivalent).

- [ ] **0.8** Pre-register required resource providers (new subscriptions don't auto-register all namespaces — doing this now avoids errors mid-provisioning):
  ```
  az provider register --namespace Microsoft.Sql --wait
  az provider register --namespace Microsoft.Storage --wait
  az provider register --namespace Microsoft.Web --wait
  ```
  Each command blocks until registration completes (1–3 minutes). Expected output: `Registered`.

**Phase 0 complete when:** `az account show` returns the correct subscription, `dotnet build` exits 0, `gh auth status` is authenticated, and all three providers show `Registered`.

---

## Phase 1 — Azure Resource Provisioning

**Prerequisite:** Phase 0 complete. All tokens resolved.

> **Edge case:** `<app-name>` and `<sql-server-name>` must be globally unique across all Azure tenants. If a command fails with "Name already exists", append a short suffix (e.g. `-x7k`) and retry.

### 1A — Resource Group

- [ ] **1.1** Create the resource group:
  ```
  az group create --name <rg-name> --location westeurope
  ```
  Expected: JSON with `"provisioningState": "Succeeded"`.

### 1B — App Service Plan and Web App

- [ ] **1.2** Create App Service plan on F1 Free tier (Linux required for DOTNETCORE runtime):
  ```
  az appservice plan create \
    --name <plan-name> \
    --resource-group <rg-name> \
    --sku F1 \
    --is-linux
  ```

- [ ] **1.3** Create the Web App with .NET 9 runtime (**use `9.0`, NOT `8.0` from infrastructure.md**):
  ```
  az webapp create \
    --name <app-name> \
    --resource-group <rg-name> \
    --plan <plan-name> \
    --runtime "DOTNETCORE:9.0"
  ```
  > **Edge case (F1 tier):** F1 has no Always On. Do not attempt to enable it — the command succeeds but the setting is silently ignored. Enable only after upgrading to B1 (step 1.10 — deferred).

  Verify the URL `https://<app-name>.azurewebsites.net` returns Azure's default "Your web app is running" page (HTTP 200).

### 1C — Azure SQL Server and Database

- [ ] **1.4** Create the SQL logical server:
  ```
  az sql server create \
    --name <sql-server-name> \
    --resource-group <rg-name> \
    --location westeurope \
    --admin-user <admin-user> \
    --admin-password "<strong-password>"
  ```
  > **Edge case:** The password appears in shell history. Store it in your password manager immediately — you'll need the exact same value for the connection string in Phase 4.

- [ ] **1.5** Create the Azure SQL Database on the free offer with auto-pause:
  ```
  az sql db create \
    --name <db-name> \
    --resource-group <rg-name> \
    --server <sql-server-name> \
    --edition GeneralPurpose \
    --family Gen5 \
    --capacity 1 \
    --compute-model Serverless \
    --use-free-limit true \
    --free-limit-exhaustion-behavior AutoPause
  ```
  > **Edge case:** `AutoPause` pauses the DB when the monthly 100k vCore-second budget is exhausted. Connections fail with SqlException error 40613 until the DB wakes up. The EF Core retry policy in Phase 2 (step 2.4) is the mitigation — do not deploy a DbContext without it.

- [ ] **1.6** Allow Azure services to reach the SQL server:
  ```
  az sql server firewall-rule create \
    --resource-group <rg-name> \
    --server <sql-server-name> \
    --name AllowAzureServices \
    --start-ip-address 0.0.0.0 \
    --end-ip-address 0.0.0.0
  ```
  > **Security:** `0.0.0.0–0.0.0.0` is the Azure-internal rule (allows Azure services only). Do **not** use `0.0.0.0–255.255.255.255` — that exposes the server to the public internet.

### 1D — Storage Account for Data Protection Key Ring

Not in `infrastructure.md` but required before any auth-related code ships. Provision now while Azure resources are being created.

- [ ] **1.7** Create Storage Account for Data Protection keys:
  ```
  az storage account create \
    --name <storage-account-name> \
    --resource-group <rg-name> \
    --location westeurope \
    --sku Standard_LRS \
    --kind StorageV2 \
    --min-tls-version TLS1_2
  ```
  Name rules: 3–24 lowercase alphanumeric only (no dashes). Suggested: `stwatermedp`.

- [ ] **1.8** Create the blob container for key files:
  ```
  az storage container create \
    --name <blob-container-name> \
    --account-name <storage-account-name> \
    --auth-mode login
  ```

- [ ] **1.9** Retrieve and store the storage connection string (for use in Phase 4):
  ```
  az storage account show-connection-string \
    --name <storage-account-name> \
    --resource-group <rg-name> \
    --query connectionString \
    --output tsv
  ```
  Store in password manager as `DataProtection__StorageConnectionString`.

### 1E — Verification

- [ ] **1.10** Confirm all resources exist:
  ```
  az resource list --resource-group <rg-name> --output table
  ```
  Expected rows: `Microsoft.Web/serverfarms`, `Microsoft.Web/sites`, `Microsoft.Sql/servers`, `Microsoft.Sql/servers/databases`, `Microsoft.Storage/storageAccounts`.

- [ ] **1.11** Confirm web app URL is reachable:
  ```
  az webapp browse --name <app-name> --resource-group <rg-name>
  ```
  Must open to Azure default page. A 404 or connection refused means the web app did not provision correctly.

### 1F — Upgrade to B1 (DEFERRED — before real user traffic)

- [ ] **1.12** *(DEFERRED)* Upgrade to B1 and enable Always On:
  ```
  az appservice plan update --name <plan-name> --resource-group <rg-name> --sku B1
  az webapp config set --name <app-name> --resource-group <rg-name> --always-on true
  az webapp config set --name <app-name> --resource-group <rg-name> \
    --generic-configurations '{"healthCheckPath": "/healthz"}'
  ```
  > **Edge case:** Always On pings `/` by default. If `/` redirects (302) to a login page, the ping doesn't count and the app still cold-starts. Setting `healthCheckPath` to `/healthz` (which returns 200 with no auth) fixes this.

**Phase 1 complete when:** `az resource list` shows all five resource types and the web app URL returns HTTP 200.

---

## Phase 2 — Code Changes for Azure Compatibility

**Prerequisite:** Phase 1 complete. Changes must be committed before Phase 5 deploy.

**Current state:** `Program.cs` is the bare `dotnet new webapi` scaffold — only the `/weatherforecast` demo endpoint. No EF Core, no Identity, no DbContext. Steps that depend on those are explicitly marked DEFERRED.

### 2.1 — ForwardedHeaders Middleware (REQUIRED NOW)

Azure terminates TLS at the load balancer. Without this middleware, `Request.IsHttps` returns false and any future auth/cookie middleware malfunctions.

Edit `Program.cs` — add `using Microsoft.AspNetCore.HttpOverrides;` at the top and insert the middleware call **before** `UseHttpsRedirection`:

```csharp
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();           // Step 2.2

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// REQUIRED: must come before UseHttpsRedirection and UseAuthentication
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();

app.MapHealthChecks("/healthz");              // Step 2.2

// ... existing weatherforecast endpoint ...

app.Run();
```

`Microsoft.AspNetCore.HttpOverrides` is part of `Microsoft.AspNetCore.App` — no new NuGet package needed.

### 2.2 — Health Check Endpoint (REQUIRED NOW)

Already shown in the code block above:
- `builder.Services.AddHealthChecks();` in the services section
- `app.MapHealthChecks("/healthz");` in the pipeline section

Returns HTTP 200 with body `Healthy`. No auth guard — this must remain publicly accessible for Always On (step 1.12).

### 2.3 — Production Log Level (REQUIRED NOW)

Create `appsettings.Production.json` at the project root. Prevents Application Insights from exceeding the 5 GB/month free cap:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

`Microsoft.Hosting.Lifetime` stays at `Information` so startup/shutdown messages appear in `az webapp log tail`.

### 2.4 — EF Core Retry Policy (DEFERRED — before first DbContext deploy)

EF Core and `DbContext` do not exist yet. When EF Core is added, the `UseSqlServer` call **must** include `.EnableRetryOnFailure()`. This handles SqlException error 40613 from Azure SQL auto-pause wake-up:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(maxRetryCount: 5)
    ));
```

Install the package when ready:
```
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
```

> **Do not deploy a DbContext without this.** The first request after auto-pause wake-up will surface as a user-visible 500 without it.

### 2.5 — Data Protection Key Ring (DEFERRED — before first auth-required deploy)

With `WEBSITE_RUN_FROM_PACKAGE=1` (set by GitHub Actions deploy), `wwwroot` is read-only. The default filesystem key storage silently fails, breaking cookie auth.

When Identity is added, install:
```
dotnet add package Azure.Extensions.AspNetCore.DataProtection.Blobs
```

Add to `Program.cs`:
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(
        new Uri(builder.Configuration["DataProtection__BlobUri"]
            ?? throw new InvalidOperationException("DataProtection__BlobUri not configured")));
```

Generate the SAS URI for the container created in step 1.8:
```
az storage container generate-sas \
  --account-name <storage-account-name> \
  --name <blob-container-name> \
  --permissions racwdl \
  --expiry 2030-01-01 \
  --auth-mode login \
  --as-user \
  --output tsv
```
Full URI: `https://<storage-account-name>.blob.core.windows.net/<blob-container-name>?<sas-token>`  
Set as App Service env var `DataProtection__BlobUri` (step 4.4).

**Alternative:** `PersistKeysToDbContext<AppDbContext>()` (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`) once EF Core is set up — simpler, no second Azure resource to manage.

### 2.6 — Verification

- [ ] **2.A** Build must pass cleanly after all Phase 2 changes:
  ```
  dotnet build --configuration Release
  ```

- [ ] **2.B** Run locally and confirm `/healthz` returns 200:
  ```
  dotnet run
  curl http://localhost:5272/healthz
  ```
  Expected: body `Healthy`, status 200.

- [ ] **2.C** Commit Phase 2 changes:
  ```
  git add Program.cs appsettings.Production.json
  git commit -m "#1.6 add Azure compatibility middleware and health check"
  ```

**Phase 2 complete when:** `dotnet build --configuration Release` exits 0 and `/healthz` returns 200 locally.

---

## Phase 3 — CI/CD Setup (GitHub Actions + OIDC)

**Prerequisite:** Phase 1 complete. Phase 2 commit on `main`.

> **Why OIDC, not publish profile:** Publish profiles are silently invalidated after ~90 days or on App Service restart. The failure is a generic 403 with no indication the credential is stale. OIDC service principals use short-lived tokens and never expire.

### 3A — Create Service Principal

- [ ] **3.1** Create the SP with Contributor role scoped to the resource group:
  ```
  az ad sp create-for-rbac \
    --name <sp-name> \
    --role contributor \
    --scopes /subscriptions/<subscription-id>/resourceGroups/<rg-name> \
    --sdk-auth
  ```
  Copy the full JSON output — note `clientId` and `tenantId`. Store in password manager. The `clientSecret` is shown only once.

### 3B — Configure OIDC Federation

- [ ] **3.2** Add a federated identity credential for the `main` branch:
  ```
  az ad app federated-credential create \
    --id <clientId-from-3.1> \
    --parameters '{
      "name": "waterme-gha-main",
      "issuer": "https://token.actions.githubusercontent.com",
      "subject": "repo:BartiDev/WaterMe:ref:refs/heads/main",
      "audiences": ["api://AzureADTokenEndpoint"]
    }'
  ```
  > **Edge case:** The `subject` string is case-sensitive and must exactly match your GitHub org/repo. If OIDC fails with `AADSTS70021: No matching federated identity record found`, the actual subject claim is printed in the workflow error output — update the federated credential with the exact string shown.

### 3C — Register GitHub Secrets

- [ ] **3.3** Set the three OIDC secrets in GitHub:
  ```
  gh secret set AZURE_CLIENT_ID --body "<clientId>" --repo BartiDev/WaterMe
  gh secret set AZURE_TENANT_ID --body "<tenantId>" --repo BartiDev/WaterMe
  gh secret set AZURE_SUBSCRIPTION_ID --body "<subscription-id>" --repo BartiDev/WaterMe
  ```

### 3D — Create GitHub Actions Workflow

- [ ] **3.4** Create `.github/workflows/deploy.yml`:

  ```yaml
  name: Deploy to Azure App Service

  on:
    push:
      branches: [ main ]
    workflow_dispatch:

  permissions:
    id-token: write   # Required for OIDC token exchange
    contents: read

  env:
    DOTNET_VERSION: '9.0.x'
    AZURE_WEBAPP_NAME: '<app-name>'
    AZURE_RESOURCE_GROUP: '<rg-name>'

  jobs:
    build-and-deploy:
      runs-on: ubuntu-latest

      steps:
        - name: Checkout
          uses: actions/checkout@v4

        - name: Setup .NET ${{ env.DOTNET_VERSION }}
          uses: actions/setup-dotnet@v4
          with:
            dotnet-version: ${{ env.DOTNET_VERSION }}

        - name: Restore
          run: dotnet restore

        - name: Build
          run: dotnet build --configuration Release --no-restore

        - name: Publish
          run: dotnet publish --configuration Release --no-build --output ./publish

        - name: Log in to Azure via OIDC
          uses: azure/login@v2
          with:
            client-id: ${{ secrets.AZURE_CLIENT_ID }}
            tenant-id: ${{ secrets.AZURE_TENANT_ID }}
            subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

        - name: Deploy to Azure App Service
          uses: azure/webapps-deploy@v3
          with:
            app-name: ${{ env.AZURE_WEBAPP_NAME }}
            package: ./publish

        - name: Verify health check
          run: |
            sleep 30
            curl --fail --silent --show-error \
              "https://${{ env.AZURE_WEBAPP_NAME }}.azurewebsites.net/healthz"
          continue-on-error: true   # Safety net for first deploy only — remove after first success
  ```

  > **Note:** `DOTNET_VERSION: '9.0.x'` matches `net9.0` in the csproj. `azure/webapps-deploy@v3` sets `WEBSITE_RUN_FROM_PACKAGE=1` implicitly — this makes `wwwroot` read-only, which is why Data Protection key ring storage (step 2.5) must be wired before any auth ships.

- [ ] **3.5** Commit the workflow:
  ```
  git add .github/workflows/deploy.yml
  git commit -m "#1.6 add GitHub Actions OIDC deploy workflow"
  ```

**Phase 3 complete when:** `.github/workflows/deploy.yml` is committed to `main` with OIDC secrets registered in GitHub.

---

## Phase 4 — App Service Configuration

**Prerequisite:** Phase 1 complete. Run before pushing in Phase 5 so the app starts with correct settings.

> **Critical rule:** Linux App Service uses `__` (double underscore) as the config separator, **not** `:`. `OpenAI:ApiKey` → `OpenAI__ApiKey`. Using `:` causes the key to not be found at runtime — the app starts without error but the feature silently fails.

### 4A — Runtime Verification

- [ ] **4.1** Verify the runtime is .NET 9 (confirm, don't assume):
  ```
  az webapp config show \
    --name <app-name> \
    --resource-group <rg-name> \
    --query linuxFxVersion \
    --output tsv
  ```
  Expected: `DOTNETCORE|9.0`. If `DOTNETCORE|8.0`, fix it:
  ```
  az webapp config set \
    --name <app-name> \
    --resource-group <rg-name> \
    --linux-fx-version "DOTNETCORE|9.0"
  ```

### 4B — Application Settings (REQUIRED NOW)

- [ ] **4.2** Set baseline app settings:
  ```
  az webapp config appsettings set \
    --name <app-name> \
    --resource-group <rg-name> \
    --settings \
      "ASPNETCORE_ENVIRONMENT=Production" \
      "WEBSITE_RUN_FROM_PACKAGE=1"
  ```

### 4C — Deferred Settings (add when corresponding feature is ready)

- [ ] **4.3** *(DEFERRED — when AI feature is added)* OpenAI API key — use `__`, not `:`:
  ```
  az webapp config appsettings set \
    --name <app-name> \
    --resource-group <rg-name> \
    --settings "OpenAI__ApiKey=<your-openai-key>"
  ```

- [ ] **4.4** *(DEFERRED — when Data Protection is wired, step 2.5)* Data Protection blob URI:
  ```
  az webapp config appsettings set \
    --name <app-name> \
    --resource-group <rg-name> \
    --settings "DataProtection__BlobUri=<sas-uri-from-step-1.9>"
  ```

- [ ] **4.5** *(DEFERRED — when EF Core / DbContext is added)* SQL connection string:
  ```
  az webapp config connection-string set \
    --name <app-name> \
    --resource-group <rg-name> \
    --connection-string-type SQLAzure \
    --settings "DefaultConnection=Server=<sql-server-name>.database.windows.net;Database=<db-name>;User Id=<admin-user>;Password=<strong-password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  ```
  Read in code with `builder.Configuration.GetConnectionString("DefaultConnection")`. App Service injects this with a `SQLAZURECONNSTR_` prefix that ASP.NET Core resolves automatically.

  > **Edge case:** `TrustServerCertificate=False` with `Encrypt=True` is correct for Azure SQL. Never set `TrustServerCertificate=True` in production.

### 4D — Verification

- [ ] **4.6** Confirm expected settings are present:
  ```
  az webapp config appsettings list \
    --name <app-name> \
    --resource-group <rg-name> \
    --output table
  ```
  Must show: `ASPNETCORE_ENVIRONMENT=Production`, `WEBSITE_RUN_FROM_PACKAGE=1`. Deferred entries absent — expected.

- [ ] **4.7** Reconfirm runtime after any config changes:
  ```
  az webapp config show --name <app-name> --resource-group <rg-name> --query linuxFxVersion
  ```
  Must still be `DOTNETCORE|9.0`.

**Phase 4 complete when:** appsettings list shows both baseline settings and `linuxFxVersion` is `DOTNETCORE|9.0`.

---

## Phase 5 — First Deploy and Verification

**Prerequisite:** Phases 1–4 complete. All Phase 2 code changes committed to `main`.

### 5A — Trigger Deploy

- [ ] **5.1** Push to main (triggers the workflow from Phase 3):
  ```
  git status
  git push origin main
  ```

- [ ] **5.2** Monitor at `https://github.com/BartiDev/WaterMe/actions`. Watch for:
  - `Log in to Azure via OIDC` — must complete without error. Failure = federation config issue (step 3.2).
  - `Deploy to Azure App Service` — must upload and swap successfully.
  - `Verify health check` — confirms app started and `/healthz` returns 200.

  > **Edge case (first cold start):** F1 tier first cold start can take 20–40 seconds. If the health check step fails on the very first deploy, re-run via `workflow_dispatch` — the second run typically succeeds because the app is already warm. Remove `continue-on-error: true` after first successful deploy.

### 5B — Smoke Tests

- [ ] **5.3** Health check returns HTTP 200:
  ```
  curl -i https://<app-name>.azurewebsites.net/healthz
  ```
  Expected: `HTTP/2 200`, body `Healthy`.

- [ ] **5.4** Demo endpoint returns JSON (confirms runtime is correct):
  ```
  curl -s https://<app-name>.azurewebsites.net/weatherforecast
  ```
  Expected: JSON array of five weather forecast objects. A 404 means the publish artifact is incomplete.

- [ ] **5.5** HTTP redirects to HTTPS (confirms ForwardedHeaders middleware is in effect):
  ```
  curl -i http://<app-name>.azurewebsites.net/healthz
  ```
  Expected: `HTTP/1.1 307` or `301` with `Location: https://...`. If this returns 200 over plain HTTP, `UseForwardedHeaders` is not before `UseHttpsRedirection` in `Program.cs`.

### 5C — Log Verification

- [ ] **5.6** Enable logging and tail:
  ```
  az webapp log config \
    --name <app-name> \
    --resource-group <rg-name> \
    --application-logging filesystem \
    --level Verbose
  az webapp log tail \
    --name <app-name> \
    --resource-group <rg-name>
  ```
  Make a request to `/healthz` while the tail runs. You should see request log lines. Note: filesystem logging auto-disables after 12 hours — re-enable with the same command when debugging.

- [ ] **5.7** Check startup log for these failure patterns:
  | Pattern | Cause | Fix |
  |---|---|---|
  | `The framework 'Microsoft.NETCore.App', version '9.0' was not found` | Runtime mismatch | Verify `linuxFxVersion` is `DOTNETCORE|9.0` (step 4.1) |
  | `Unable to find a required file` | Incomplete publish artifact | Check `dotnet publish --output ./publish` step |
  | `System.IO.IOException: Read-only file system` | Data Protection writing to wwwroot | Wire step 2.5 before deploying any auth code |
  | `SqlException 40613` without retry loop | EF Core missing retry policy | Wire step 2.4 before deploying any DbContext |

### 5D — Final Confirmation

- [ ] **5.8** Open the live URL from a different network (phone hotspot) to confirm public accessibility:  
  `https://<app-name>.azurewebsites.net`

**Phase 5 complete when:** `/healthz` returns 200, `/weatherforecast` returns JSON, HTTP redirects to HTTPS, and log tail shows request activity.

---

## Deferred Steps — Checklist for When Each Feature Ships

| Step | When to run |
|---|---|
| EF Core retry policy (step 2.4) | Before first DbContext commit |
| Data Protection key ring (step 2.5) | Before first auth/cookie commit |
| `DataProtection__BlobUri` app setting (step 4.4) | Same as above |
| SQL connection string (step 4.5) | When DbContext is added |
| `OpenAI__ApiKey` app setting (step 4.3) | When AI feature is wired |
| Always On + health check path on B1 (step 1.12) | Before inviting real users |

---

## Risk Register — All Items Addressed

| Risk (from infrastructure.md) | Addressed in |
|---|---|
| Azure SQL auto-pause → SqlException 40613 | Step 2.4 — EF Core retry policy (deferred) |
| Missing UseForwardedHeaders breaks HTTPS/auth | Step 2.1 — required now, before first deploy |
| Linux `__` config separator causes silent failures | Phase 4 — all appsettings commands use `__` |
| Publish profile expires → GitHub Actions breaks | Phase 3 — OIDC from day one, no publish profile |
| Data Protection key ring fails on read-only wwwroot | Steps 1.7–1.9 + step 2.5 (deferred) |
| Always On pings `/` which redirects (302) | Step 2.2 — `/healthz` returns 200; step 1.12 sets `healthCheckPath` |
| No deployment slots on B1 — risky migration | Acknowledged; rollback = re-run workflow on previous SHA |
| Application Insights log volume exceeds 5 GB | Step 2.3 — `Warning` level in `appsettings.Production.json` |

---

## Critical Files Modified

| File | Phase | Change |
|---|---|---|
| `Program.cs` | Phase 2 | ForwardedHeaders middleware, health check endpoint |
| `appsettings.Production.json` | Phase 2 | Warning log level for production (new file) |
| `.github/workflows/deploy.yml` | Phase 3 | OIDC deploy workflow (new file) |
