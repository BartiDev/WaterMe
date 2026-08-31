# EF Core + Identity Infrastructure — Plan Brief

> Full plan: `context/changes/persistence-auth-scaffold/plan.md`

## What & Why

F-01 wires the persistence and auth infrastructure that every subsequent slice depends on: EF Core + ASP.NET Core Identity on top of the existing ASP.NET Core 9.0 shell, with cookies as the auth scheme and a global "require auth" policy enforced at the framework level. Without it, no user data can be persisted and no route can be protected — S-01 (account flow) cannot proceed.

## Starting Point

The project is a bare ASP.NET Core 9.0 minimal-API shell with one NuGet package (`Microsoft.AspNetCore.OpenApi`), a weatherforecast demo endpoint, and no database, DbContext, Identity, or auth middleware. ForwardedHeaders and health checks are already registered (`Program.cs:10,14`), which avoids two known Azure deployment gotchas.

## Desired End State

A developer running `dotnet run` starts the app successfully with no exceptions; any unauthenticated request to a non-health-check route receives a 302 redirect to `/account/login`. Running `dotnet ef database update` creates `waterme.db` with 7 Identity tables plus `DataProtectionKeys`. The auth middleware stack is fully active and all subsequent slices can rely on `[Authorize]` and `ClaimsPrincipal.Identity.IsAuthenticated` being correctly evaluated.

## Key Decisions Made

| Decision | Choice | Why |
|---|---|---|
| Data Protection key storage | DB table (`PersistKeysToDbContext`) | Same DB already being set up; no extra Azure Storage Account required |
| DbContext scope | Identity tables only | F-01 boundary; Plant entity designed and added in S-02 |
| Authorization boundary | Global fallback policy (`options.FallbackPolicy = options.DefaultPolicy`) | Fails-safe: new routes require auth by default; PRD treats data isolation as an absolute constraint |
| Migration workflow | Developer-run only (`dotnet ef database update`) | B1 Basic has no deployment slots; safest rollback posture per `infrastructure.md` |
| Cookie lifetime | Explicit 1-day sliding; `CookieSecurePolicy.SameAsRequest` | Production-safe with `UseForwardedHeaders` already active; avoids SDK default drift |
| Demo endpoint | Removed in this change | Would need explicit `[AllowAnonymous]` under the global policy; removal keeps the surface clean |

## Scope

**In scope:**
- 5 NuGet packages: EF Core SqlServer + Sqlite + Tools, Identity EF Core, DataProtection EF Core
- `Data/ApplicationDbContext.cs` — extends `IdentityDbContext<IdentityUser>` + implements `IDataProtectionKeyContext`
- `appsettings.Development.json` — SQLite connection string added
- Initial EF Core migration (`Migrations/InitialCreate`) — 8 tables total
- `Program.cs` overhaul — DbContext + Identity + DataProtection + Authorization services; auth middleware; demo cleanup; health check exempted with `.AllowAnonymous()`

**Out of scope:**
- Plant entity or any domain model (S-02)
- Sign-in / sign-up pages (S-01)
- Email confirmation, OAuth providers
- CI/CD migration automation
- Azure Blob Storage for key storage

## Architecture / Approach

`ApplicationDbContext` extends `IdentityDbContext<IdentityUser>` and implements `IDataProtectionKeyContext`, giving it ownership of all Identity tables plus `DataProtectionKeys` in a single migration. Service registrations in `Program.cs` follow a strict ordering: DbContext (env-conditional) → `AddIdentity` → `ConfigureApplicationCookie` (must follow `AddIdentity`) → `AddDataProtection` → `AddAuthorization` (fallback policy). Middleware additions land between the existing `UseHttpsRedirection` and `MapHealthChecks` calls: `UseAuthentication` then `UseAuthorization`. The health check opts out of the global policy with `.AllowAnonymous()`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Data Layer | Working DbContext; migration files; `waterme.db` with 8 tables | `dotnet-ef` global tool not installed → migration command fails |
| 2. Auth Wiring | Auth middleware active; global policy enforced; `/healthz` public; demo removed | `ConfigureApplicationCookie` called before `AddIdentity` → silent no-op on cookie options |

**Prerequisites:** `dotnet-ef` global tool installed (`dotnet tool install --global dotnet-ef`)
**Estimated effort:** ~1–2 hours across 2 phases

## Open Risks & Assumptions

- Production Azure SQL connection string must be set in App Service Configuration before first prod deploy; the app throws `InvalidOperationException` on startup if missing (intentional fail-fast)
- The `DataProtectionKeys` table must exist before `dotnet run` — skipping Phase 1 database update causes a startup exception in Phase 2
- `/account/login` redirect target is configured in F-01 but the page doesn't exist until S-01; auth redirects return a 404 until then

## Success Criteria (Summary)

- `dotnet build` passes cleanly after both phases
- `GET /healthz` returns 200; unauthenticated `GET /` returns 302 to `/account/login`
- `waterme.db` contains Identity (7 tables) + `DataProtectionKeys` after `dotnet ef database update`
