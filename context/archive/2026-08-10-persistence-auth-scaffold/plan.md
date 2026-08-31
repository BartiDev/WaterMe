# EF Core + Identity Infrastructure — Implementation Plan

## Overview

Wire EF Core and ASP.NET Core Identity into the existing ASP.NET Core 9.0 shell to deliver the persistence and auth foundation (F-01) that all subsequent slices depend on. The app currently has no database, no auth, and no domain models. After this plan: a SQLite-backed local database, a single EF Core migration covering Identity and Data Protection tables, and a fully active auth middleware stack with a global auth requirement.

## Current State Analysis

The project is a bare ASP.NET Core 9.0 minimal-API shell (`Program.cs`). It has OpenAPI, health checks, and ForwardedHeaders middleware already wired. There is no EF Core, no Identity, no DbContext, no connection strings, and no migrations. The only NuGet package is `Microsoft.AspNetCore.OpenApi`. The weatherforecast demo endpoint and its `WeatherForecast` record type are the only application-level code and will be removed in Phase 2.

## Desired End State

- `Data/ApplicationDbContext.cs` exists, extending `IdentityDbContext<IdentityUser>` and implementing `IDataProtectionKeyContext`
- `Migrations/` folder contains the `InitialCreate` migration capturing Identity (7 tables) + `DataProtectionKeys` (1 table)
- `appsettings.Development.json` has a SQLite `DefaultConnection`; the production connection string is supplied via Azure App Service Configuration (not in source)
- `Program.cs`: DbContext registered env-conditionally; Identity, DataProtection, and Authorization services registered; auth middleware active
- Unauthenticated requests to any route other than `/healthz` receive a 302 redirect to `/account/login`
- `/healthz` returns 200 without authentication

### Key Discoveries

- `Program.cs:10` — `UseForwardedHeaders` already registered; middleware additions (UseAuthentication, UseAuthorization) insert after `UseHttpsRedirection`, not before ForwardedHeaders
- `Program.cs:14` — health check mapping exists; needs `.AllowAnonymous()` added when global auth policy is active
- `water-me.csproj:9` — only `Microsoft.AspNetCore.OpenApi` present; 5 new packages needed
- `appsettings.Production.json` — already scoped to `Warning`-level logging, aligned with infrastructure.md guidance
- No `.gitignore` exclusion for `*.db` files confirmed; this should be verified and added if absent

## What We're NOT Doing

- No Plant entity or domain models — DbContext carries Identity tables only; domain schema is S-02's concern
- No email confirmation flow — `RequireConfirmedAccount = false` for MVP
- No role-based authorization — flat user model; no role checks beyond what Identity scaffolds by default
- No CI migration step — migrations are developer-run only; prod schema changes are applied manually before each deploy
- No auto-apply on startup — `context.Database.MigrateAsync()` is NOT called at app start; migration is a deliberate developer action
- No Azure Blob Storage for Data Protection keys — keys are stored in the same database via `PersistKeysToDbContext`

## Implementation Approach

Two phases in dependency order: (1) land the data layer so the migration and database exist before the app starts, then (2) wire auth into `Program.cs`. The entire F-01 outcome lands without a frontend — there is no sign-in page yet (that's S-01). The auth redirect target `/account/login` is configured now but the actual page is built in S-01; until then, auth redirects land on a 404.

## Critical Implementation Details

**`IDataProtectionKeyContext` is a required interface on the DbContext**: `PersistKeysToDbContext<ApplicationDbContext>()` compiles regardless of whether `ApplicationDbContext` implements `IDataProtectionKeyContext`, but throws a runtime exception on first cookie operation if the interface (and its `DbSet<DataProtectionKey> DataProtectionKeys` property) is absent. The implementation and the migration must both be in place before the app starts.

**`ConfigureApplicationCookie` must be called after `AddIdentity`**: `AddIdentity` registers the `Identity.Application` cookie auth scheme; `ConfigureApplicationCookie` post-configures options on that scheme. Calling it before `AddIdentity` silently discards all cookie option customizations — the app appears to work but cookie lifetime, secure flag, and login path are not applied.

**`UseAuthentication()` must precede `UseAuthorization()`**: Wrong ordering causes authorization middleware to see an unauthenticated principal on every request, silently failing all auth checks. Both calls also follow the existing `UseForwardedHeaders()` and `UseHttpsRedirection()` calls — do not reorder those.

**The migration must be applied before first app start**: `PersistKeysToDbContext` queries the `DataProtectionKeys` table on startup. If the migration hasn't been applied, the app throws a database exception during startup (not on first user request). Complete Phase 1 manual verification (run `dotnet ef database update`) before running the app in Phase 2.

---

## Phase 1: Data Layer

### Overview

Add the 5 NuGet packages, create `ApplicationDbContext`, add the SQLite dev connection string, and generate the initial migration. Phase 1 is complete when `dotnet ef database update` creates a local `waterme.db` with the expected tables.

Prerequisite: `dotnet-ef` global tool must be installed before running migration commands (`dotnet tool install --global dotnet-ef`).

### Changes Required

#### 1. NuGet packages

**File**: `water-me.csproj`

**Intent**: Add the EF Core and Identity packages the app needs. Five packages total: SQL Server provider (production), SQLite provider (development), EF Core design-time CLI tools (migration generation), Identity EF Core integration, and DataProtection EF Core integration.

**Contract**: Add five `PackageReference` entries, all version-aligned with the project's `net9.0` target (9.x.x at time of writing):
- `Microsoft.EntityFrameworkCore.SqlServer`
- `Microsoft.EntityFrameworkCore.Sqlite`
- `Microsoft.EntityFrameworkCore.Tools` — add `<PrivateAssets>all</PrivateAssets>` so this design-time-only package is excluded from the publish output
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`

#### 2. ApplicationDbContext

**File**: `Data/ApplicationDbContext.cs` (new file; create the `Data/` folder)

**Intent**: Create the single EF Core context for the application. It extends `IdentityDbContext<IdentityUser>` to inherit the full Identity schema, and implements `IDataProtectionKeyContext` so `PersistKeysToDbContext<ApplicationDbContext>()` can store the key ring in the same database.

**Contract**: Class `ApplicationDbContext` in namespace `water_me`. Extends `IdentityDbContext<IdentityUser>` (from `Microsoft.AspNetCore.Identity.EntityFrameworkCore`). Implements `IDataProtectionKeyContext` (from `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`), which requires one property: `public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }`. Standard constructor: takes `DbContextOptions<ApplicationDbContext>`, passes to base.

#### 3. Dev connection string

**File**: `appsettings.Development.json`

**Intent**: Supply the SQLite connection string for local development so the env-conditional DbContext registration can resolve `DefaultConnection` in the Development environment.

**Contract**: Add a `"ConnectionStrings"` object with key `"DefaultConnection"` and value `"Data Source=waterme.db"`. The `waterme.db` file is created at the project root by `dotnet ef database update`. Verify `.gitignore` excludes `*.db` files; add the rule if absent.

#### 4. Initial migration

**Not a file edit — CLI command to run after changes 1–3 are in place:**

Run `dotnet ef migrations add InitialCreate` from the project root. This generates two files:
- `Migrations/[timestamp]_InitialCreate.cs` — the up/down schema definition
- `Migrations/ApplicationDbContextModelSnapshot.cs` — the model snapshot used by subsequent migration diffs

The migration will capture the full Identity schema (7 tables: `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`) plus `DataProtectionKeys`.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with all 5 new packages resolved and no compiler errors

#### Manual Verification

- `dotnet ef migrations add InitialCreate` completes without errors; `Migrations/` folder is created with migration files
- `dotnet ef database update` applies cleanly; `waterme.db` contains all 8 expected tables (7 Identity + `DataProtectionKeys`)

**Pause here for manual confirmation before proceeding to Phase 2.**

---

## Phase 2: Auth Wiring

### Overview

Update `Program.cs` to register the DbContext, Identity, DataProtection, and Authorization services; configure explicit cookie auth options; insert the auth middleware pair into the pipeline; exempt the health check from auth; and remove the weatherforecast demo. Phase 2 is complete when the app starts cleanly and enforces auth on all routes except `/healthz`.

### Changes Required

#### 1. Remove demo endpoint

**File**: `Program.cs`

**Intent**: Remove the `GET /weatherforecast` endpoint and the `WeatherForecast` record type. Without removal, the global auth policy would silently require authentication on the demo endpoint, creating confusion for anyone testing with curl. Removal keeps the app's surface intentional.

**Contract**: Delete the `var summaries = string[]` array, the `app.MapGet("/weatherforecast", ...)` call, and the `record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)` type declaration at the bottom of the file.

#### 2. Register DbContext

**File**: `Program.cs`

**Intent**: Register `ApplicationDbContext` with DI. Use SQLite in development (zero external dependencies) and SQL Server with retry logic in production to handle Azure SQL auto-pause recovery (documented as a known risk in `context/foundation/infrastructure.md`).

**Contract**: Read the connection string with `builder.Configuration.GetConnectionString("DefaultConnection")` and throw `InvalidOperationException` if null (fail-fast on misconfiguration). Env-conditional `AddDbContext<ApplicationDbContext>` registration:
- `IsDevelopment()` branch: `UseSqlite(connectionString)`
- Else branch: `UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure(maxRetryCount: 5))`

#### 3. Register Identity

**File**: `Program.cs`

**Intent**: Wire ASP.NET Core Identity services and connect them to the DbContext. `AddDefaultTokenProviders()` is included now because password-reset token generation (needed in S-01) requires it, and adding it later would require a re-registration.

**Contract**: `AddIdentity<IdentityUser, IdentityRole>()` with options setting `SignIn.RequireConfirmedAccount = false` and default password requirements (`RequireDigit = true`, `RequireLowercase = true`, `RequireUppercase = true`, `RequireNonAlphanumeric = true`, `RequiredLength = 6`). Chain `.AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders()`.

#### 4. Configure cookie auth options

**File**: `Program.cs`

**Intent**: Set explicit, production-safe cookie auth options on the Identity application cookie scheme. Explicit configuration is preferable to relying on framework defaults that could shift across SDK versions.

**Contract**: `ConfigureApplicationCookie()` called immediately after the `AddIdentity` chain (ordering constraint — see Critical Implementation Details). Options:
- `LoginPath = "/account/login"` (redirect target for unauthenticated requests; page built in S-01)
- `SlidingExpiration = true`
- `ExpireTimeSpan = TimeSpan.FromDays(1)`
- `Cookie.HttpOnly = true`
- `Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest` (effectively `Always` in production because `UseForwardedHeaders` correctly sets `Request.IsHttps`)
- `Cookie.SameSite = SameSiteMode.Lax`

#### 5. Register DataProtection

**File**: `Program.cs`

**Intent**: Persist the Data Protection key ring to the database so authentication cookies survive application restarts on Azure. Without this, every app restart on Azure App Service invalidates all active sessions, forcing users to sign in again after each deploy.

**Contract**: `AddDataProtection().PersistKeysToDbContext<ApplicationDbContext>()`. Must be registered after `AddDbContext<ApplicationDbContext>` so the context is available in DI.

#### 6. Register Authorization with global fallback policy

**File**: `Program.cs`

**Intent**: Require authentication on all routes by default so data isolation (a PRD absolute constraint) is enforced at the framework level. New routes added in later slices are protected without any additional annotation; public routes explicitly opt out.

**Contract**: `AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy)`. `options.DefaultPolicy` resolves to `RequireAuthenticatedUser` by ASP.NET Core default.

#### 7. Add auth middleware

**File**: `Program.cs`

**Intent**: Activate authentication and authorization checks in the request pipeline. Without these two calls, all `[Authorize]` attributes and the fallback policy registered above are no-ops.

**Contract**: Insert `app.UseAuthentication()` followed immediately by `app.UseAuthorization()`, after the existing `app.UseHttpsRedirection()` and before `app.MapHealthChecks(...)`.

#### 8. Exempt health check from auth

**File**: `Program.cs`

**Intent**: Allow the `/healthz` endpoint to respond 200 without credentials. Azure App Service always-on pings and the GitHub Actions health check step in `deploy.yml` both need an unauthenticated 200 response; without this, the global fallback policy blocks them.

**Contract**: Change `app.MapHealthChecks("/healthz")` to `app.MapHealthChecks("/healthz").AllowAnonymous()`.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds after all Phase 2 changes
- `dotnet run` starts without throwing startup exceptions

#### Manual Verification

- `curl -v http://localhost:[port]/healthz` returns `HTTP/1.1 200 OK`
- `curl -v http://localhost:[port]/` (or any other non-healthz path) returns `HTTP/1.1 302 Found` with `Location: /account/login` in the response headers

**Pause here for manual confirmation before committing.**

---

## Migration Notes

Migrations are developer-run only. There is no CI step and no auto-apply on startup.

**Dev workflow**: After Phase 1, run `dotnet ef database update` once to create `waterme.db` locally. The file is created at the project root and must be excluded from git (add `*.db` to `.gitignore`). For subsequent schema changes (S-02 adds the Plant entity), run `dotnet ef migrations add <MigrationName>` then `dotnet ef database update`.

**Production deploy**: Before each deploy that includes a new migration, apply the migration to the Azure SQL database manually using the generated SQL script or `dotnet ef database update` pointed at the production connection string. Per `context/foundation/infrastructure.md` operational story: "Run migrations in a separate step before deploying new code; keep migration scripts reversible."

## References

- Roadmap F-01 outcome and risk: `context/foundation/roadmap.md:49–60`
- Azure SQL auto-pause and retry: `context/foundation/infrastructure.md:75–76`, `93`
- Data Protection key-ring risk on App Service: `context/foundation/infrastructure.md:77`, `97`
- ForwardedHeaders and HTTPS gotcha: `context/foundation/infrastructure.md:63–66`
- Current `Program.cs`: `Program.cs:1–35`
- Current `water-me.csproj`: `water-me.csproj:1–12`
- Current `appsettings.Development.json`: `appsettings.Development.json:1–8`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Data Layer

#### Automated

- [x] 1.1 `dotnet build` succeeds with all 5 new packages resolved and no compiler errors — 639278a

#### Manual

- [x] 1.2 `dotnet ef migrations add InitialCreate` completes without errors; migration files appear in `Migrations/` — 639278a
- [x] 1.3 `dotnet ef database update` applies cleanly; `waterme.db` contains Identity and DataProtectionKeys tables — 639278a

### Phase 2: Auth Wiring

#### Automated

- [x] 2.1 `dotnet build` succeeds after all Program.cs changes — 5145de9
- [x] 2.2 `dotnet run` starts without startup exceptions — 5145de9

#### Manual

- [x] 2.3 `GET /healthz` returns 200 OK without credentials — 5145de9
- [x] 2.4 Unauthenticated GET to any non-health-check route returns 302 to `/account/login` — 5145de9
