---
project: Water Me
researched_at: 2026-07-08
recommended_platform: Azure App Service
runner_up: Railway
context_type: mvp
tech_stack:
  language: C# (.NET 8)
  framework: ASP.NET Core
  runtime: .NET 8 (LTS)
  database: Azure SQL Database (free offer)
---

## Recommendation

**Deploy on Azure App Service (F1 Free while building → B1 Basic for real users).**

Azure App Service runs ASP.NET Core natively — no Docker image, no container setup. It is the Microsoft-default hosting platform for .NET, which means every official tutorial, GitHub Actions template, and NuGet tool is written for this combination. The **F1 Free tier** ($0/month) is a legitimate starting point for development and early testing; upgrade to **B1 Basic (~$13/month)** before inviting real users, because F1 lacks Always On (app sleeps after 20 minutes of inactivity — cold starts can take 5–10 seconds, which violates the PRD's 5-second AI latency cap). The Azure SQL Database free offer is permanent and works on both tiers, so database cost stays at $0 throughout.

## Platform Comparison

Hard-filtered platforms (do not support ASP.NET Core server-side — eliminated before scoring):

| Platform | Reason for elimination |
|---|---|
| Cloudflare Workers/Pages | V8 isolate runtime only; no .NET CLR support |
| Vercel | Node.js/Python/Go/Ruby runtimes only; .NET unsupported officially |
| Netlify | AWS Lambda-backed functions; Node.js/Go only |

Remaining platforms scored against five agent-friendly criteria (Pass / Partial / Fail):

| Platform | CLI-first | Managed / Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Weighted score |
|---|---|---|---|---|---|---|
| **Azure App Service** | Pass | Pass | Partial | Pass | Partial | **4.5 / 5** |
| Railway | Pass | Pass | Partial | Pass | Fail | 4 / 5 |
| Fly.io | Pass | Partial | Partial | Pass | Fail | 3.5 / 5 |
| Render | Partial | Pass | Partial | Partial | Fail | 2.5 / 5 |

Interview weights applied:
- Cost-sensitive → Azure SQL free offer (permanent) closes the cost gap vs. cheaper platforms
- Azure familiarity → tie-breaker over Railway
- Single region → no edge-native bonus applied
- Co-located DB preferred → Azure SQL in the same region scores highest

### Shortlisted Platforms

#### 1. Azure App Service (Recommended)

Native .NET runtime support without Docker. Azure SQL Database free offer provides permanent zero-cost database at MVP traffic levels. GitHub Actions has a first-class Microsoft-maintained `.NET to Azure App Service` workflow template. `az` CLI covers every deployment, configuration, and logging operation. The Azure MCP Server (community/incubation, not GA as of 2026-07-08) provides read-oriented tooling for Azure resources when available.

#### 2. Railway

Railway's Nixpacks build system auto-detects a `.csproj` file and builds the app without a Dockerfile — the closest alternative for .NET simplicity. Always-on by default (no auto-pause). Built-in `railway rollback` command. Co-located Postgres plugin available. Main gaps: Hobby plan ($5/mo base) has no automated Postgres backups; the user must bind on `$PORT` env var instead of a hardcoded port; less .NET-specific community documentation than Azure.

#### 3. Fly.io

Cheapest all-in cost (~$6–8/month). Clean `flyctl` CLI. Biggest friction point: requires a Dockerfile (Azure and Railway can deploy without one). Fly Postgres is self-managed — no automatic minor-version patching, no point-in-time restore unless manually configured. Free compute tier was removed for new accounts in late 2024. Cold-start latency on .NET JIT apps (3–6 s) would violate the PRD's 5-second AI suggestion latency cap unless always-on mode is enabled (which increases cost).

## Anti-Bias Cross-Check: Azure App Service

### Devil's Advocate — Weaknesses

1. **Azure SQL free offer auto-pauses without warning.** When the 100,000 vCore-seconds/month budget is consumed, the database pauses instantly. Connections fail with a transient `SqlException`. Without an explicit EF Core retry policy, the app surfaces a 500 error to users with no self-healing.
2. **No deployment slots below Standard tier.** B1 Basic has no staging slot, so rollback means manually re-deploying a previous artifact — there is no one-command revert. The first tier with blue-green swap is S1 Standard at ~$57/month.
3. **Forwarded-headers middleware is required but non-obvious.** TLS terminates at the Azure load balancer. Without `app.UseForwardedHeaders()`, ASP.NET Core cannot detect HTTPS, `Request.IsHttps` returns false, cookies lose the `Secure` flag, and auth redirects loop.
4. **Linux App Service uses `__` (double underscore) as the config key separator, not `:`.** A setting named `OpenAI:ApiKey` in `appsettings.json` must be set as `OpenAI__ApiKey` in Azure App Service environment variables. Using `:` causes the key to not be found at runtime — the app starts without error but the AI feature silently fails.
5. **Publish-profile credentials expire and are silently invalidated.** GitHub Actions deployments using a publish profile XML credential break when the App Service is restarted, redeployed to a new plan, or after ~90 days. OIDC-based authentication (service principal) is the durable approach.

### Pre-Mortem — How This Could Fail

The team ships WaterMe on Azure App Service B1 with Azure SQL free offer. The first two weeks go fine. On a day when friends and family are invited to test, the SQL free offer hits its 100,000 vCore-seconds monthly ceiling mid-afternoon. The database auto-pauses and all requests fail with 500 errors — there is no Application Insights configured yet, so the team diagnoses from browser error screens rather than logs. A hotfix deploy via GitHub Actions fails because the publish profile credential was regenerated earlier and the old secret in GitHub was never updated. While the team sorts out the credential, someone pushes a config change with `OpenAI:ApiKey` using a colon separator — the AI watering-schedule feature stays broken for two more hours until the double-underscore issue is spotted. By month two, the team wants to deploy a database migration safely; there are no deployment slots on B1, so the migration runs live against production with no rollback path. None of these problems are unique to Azure — they are the classic first-production-deployment failure modes that every team hits once.

### Unknown Unknowns

- **Azure SQL auto-pause is a connection failure, not a timeout.** The exception type is `SqlException` with error number 40613 ("Database is currently unavailable"). EF Core's default behaviour does not retry this — you must call `.EnableRetryOnFailure()` in `UseSqlServer()` or the first request after each auto-pause surfaces as a user-visible error.
- **Always On only works if the root URL returns HTTP 200.** The always-on mechanism sends a GET request to `/`. If `/` redirects to a login page (302), the ping is not counted as a successful keep-alive and the app still cold-starts after 20 minutes of real traffic idleness.
- **`WEBSITE_RUN_FROM_PACKAGE=1` makes the wwwroot directory read-only.** The default GitHub Actions deploy sets this flag. ASP.NET Core Data Protection writes key-ring files to the local filesystem by default; this silently fails on startup, breaking cookie authentication and anti-forgery tokens. Store the key ring in Azure Blob Storage or a database table from day one.
- **Application Insights free tier caps at 5 GB/month.** Verbose ASP.NET Core logging (the framework default for `Information` level) easily exceeds this. Configure sampling or raise the minimum log level to `Warning` in production before enabling Application Insights.
- **GitHub Actions publish-profile auth has no expiry warning.** When it fails, the error message is a generic 403 with no indication that the credential is stale. Set up OIDC from the beginning to avoid this class of incident.

## Operational Story

- **Preview deploys**: Azure App Service does not create preview URLs per pull request by default. Deployment slots (available from Standard tier, ~$57/mo) can serve as a staging environment; on B1 Basic, preview testing is done by deploying to the single production slot. For MVP, this means testing in production or running a second (free F1) App Service as a dev environment.
- **Secrets**: Environment variables and connection strings are stored in App Service Configuration (portal or `az webapp config appsettings set`). They are encrypted at rest, visible only to users with Contributor or Owner RBAC on the App Service. Rotation: update the value in App Service Configuration, then restart the app (`az webapp restart`). Never store secrets in `appsettings.json` or commit them to source control.
- **Rollback**: On B1 Basic (no slots), rollback means re-running the GitHub Actions workflow pointing at the previous commit SHA, or manually running `az webapp deploy` with a previously built zip artifact. Typical time-to-revert: 3–5 minutes. Important caveat: database migrations do not roll back automatically — a failed migration requires a manual SQL script to reverse the schema change before redeploying the old code.
- **Approval**: Actions that require a human: creating or deleting the App Service plan, rotating primary secrets, dropping the Azure SQL database, changing the pricing tier. Actions an agent may perform unattended: deploying new code via `az webapp deploy`, updating environment variables, restarting the app, tailing logs.
- **Logs**: Read application logs with `az webapp log tail --resource-group <rg> --name <app>`. Note: filesystem logging auto-disables after 12 hours — re-enable with `az webapp log config --application-logging filesystem --level Verbose`. For persistent log storage, configure Application Insights or stream to an Azure Storage account.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Azure SQL auto-pause causes 500 errors at month-end | Devil's advocate | M | H | Call `.EnableRetryOnFailure()` in `UseSqlServer()`; alternatively use Azure SQL DTU Basic ($5/mo, no auto-pause) once traffic justifies it |
| Missing `UseForwardedHeaders()` breaks HTTPS and auth | Devil's advocate | M | H | Add middleware in `Program.cs` before auth middleware; verify with `Request.IsHttps` in a test endpoint |
| Linux `__` separator causes silent config failures | Devil's advocate | H | M | Document the rule in AGENTS.md; validate every env var name at first deploy |
| Publish profile expires, GitHub Actions deploy breaks | Unknown unknowns | M | M | Use OIDC service-principal auth from day one instead of publish profile |
| Data Protection key ring write fails (read-only wwwroot) | Unknown unknowns | M | H | Configure `PersistKeysToAzureBlobStorage()` or `PersistKeysToDbContext()` before first auth-required deploy |
| Always On does not keep app warm if `/` returns 302 | Unknown unknowns | M | L | Map a `/healthz` endpoint that returns 200 and set it as the always-on path |
| No deployment slots on B1 — risky DB migration rollout | Pre-mortem | L | H | Run migrations in a separate step before deploying new code; keep migration scripts reversible |
| Application Insights log volume exceeds free 5 GB cap | Unknown unknowns | L | L | Set minimum log level to `Warning` in production `appsettings.Production.json` |

## Getting Started

These steps assume you have an Azure account and have installed the Azure CLI. If you haven't installed the CLI yet, download it from `aka.ms/installazurecli` and run `az login` to sign in.

**Step 1 — Create the Azure resources (run once)**

Start on the **F1 Free tier** while building; upgrade to B1 when you're ready for real users.

```bash
# Replace the values in angle brackets with your own names
az group create --name <rg-name> --location westeurope

# Option A — F1 Free (for development, $0/month, no Always On)
az appservice plan create --name <plan-name> --resource-group <rg-name> --sku F1 --is-linux
az webapp create --name <app-name> --resource-group <rg-name> --plan <plan-name> --runtime "DOTNETCORE:8.0"

# Option B — B1 Basic (for real users, ~$13/month, Always On available)
# az appservice plan create --name <plan-name> --resource-group <rg-name> --sku B1 --is-linux
# az webapp create --name <app-name> --resource-group <rg-name> --plan <plan-name> --runtime "DOTNETCORE:8.0"
# az webapp config set --name <app-name> --resource-group <rg-name> --always-on true
```

To upgrade from F1 to B1 later (one command, no redeployment needed):

```bash
az appservice plan update --name <plan-name> --resource-group <rg-name> --sku B1
az webapp config set --name <app-name> --resource-group <rg-name> --always-on true
```

What each command does: the first creates a logical container (resource group) for all your Azure resources. The second creates the hosting plan. The third creates the actual web app slot. Always On (B1 only) prevents the app from sleeping after 20 minutes of inactivity — required before real users start testing.

**Step 2 — Create the Azure SQL Database (free offer)**

```bash
az sql server create --name <sql-server-name> --resource-group <rg-name> --location westeurope --admin-user <admin-user> --admin-password <strong-password>
az sql db create --name <db-name> --resource-group <rg-name> --server <sql-server-name> --free-limit-exhaustion-behavior AutoPause --use-free-limit true --edition GeneralPurpose --family Gen5 --capacity 1 --compute-model Serverless
az sql server firewall-rule create --resource-group <rg-name> --server <sql-server-name> --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
```

The last firewall command allows Azure services (your App Service) to reach the SQL server. Do not add `0.0.0.0–255.255.255.255` — that would expose the server to the public internet.

**Step 3 — Set environment variables on the App Service**

Use double underscores (`__`) for nested keys — this is required for Linux App Service. A colon (`:`) will not work.

```bash
az webapp config appsettings set --name <app-name> --resource-group <rg-name> --settings \
  "OpenAI__ApiKey=<your-openai-key>" \
  "ASPNETCORE_ENVIRONMENT=Production"

# Connection string (use Custom type for PostgreSQL; use SQLAzure type for SQL Server)
az webapp config connection-string set --name <app-name> --resource-group <rg-name> \
  --connection-string-type SQLAzure \
  --settings "DefaultConnection=Server=<sql-server-name>.database.windows.net;Database=<db-name>;User Id=<admin-user>;Password=<strong-password>;Encrypt=True;"
```

**Step 4 — Add required middleware to `Program.cs`**

Before your first deploy, add these two lines to `Program.cs`. They are required for the app to work correctly behind Azure's load balancer:

```csharp
// Add BEFORE app.UseHttpsRedirection() and app.UseAuthentication()
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
```

And add a health check endpoint so Always On has a URL that returns 200:

```csharp
app.MapHealthChecks("/healthz");
// In services: builder.Services.AddHealthChecks();
```

**Step 5 — Add EF Core retry policy for Azure SQL auto-pause**

In your `DbContext` registration in `Program.cs`, add `.EnableRetryOnFailure()`. This makes EF Core automatically retry the first request after the database wakes up from auto-pause:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(maxRetryCount: 5)));
```

**Step 6 — Set up GitHub Actions with OIDC (recommended over publish profile)**

In the Azure portal, go to your App Service → Deployment Center → GitHub Actions. Select your repository and branch. When prompted for auth type, choose **User-assigned identity** or **Service principal** rather than publish profile. This generates a `.github/workflows/` file in your repo automatically and avoids the publish-profile expiry problem. Pushes to `main` will trigger an automatic deploy.

**Step 7 — Configure Data Protection key storage**

Add this NuGet package: `Azure.Extensions.AspNetCore.DataProtection.Blobs`

Then in `Program.cs`:

```csharp
// Keys are stored in Azure Blob Storage so they survive app restarts
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(new Uri("<blob-container-sas-url>"));
```

Alternatively, use `PersistKeysToDbContext<AppDbContext>()` (requires `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`) to store keys in the database — simpler if you already have EF Core set up.

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration
- CI/CD pipeline setup beyond GitHub Actions basics
- Production-scale architecture (multi-region, HA, DR)
- Azure Kubernetes Service or Azure Container Apps
- Cost optimisation beyond MVP traffic levels
