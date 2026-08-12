# Core MVP Loop Implementation Plan

## Overview

Deliver S-02: the full 6-step MVP flow on top of the completed S-01 account foundation. A signed-in user can add a plant by species name, receive an AI-suggested watering schedule (frequency + amount) inline on the same page, edit and save it, see all their plants in a list with live countdown status, mark a plant as watered, and undo that action within 10 seconds. OpenAI provides the schedule suggestions via a structured JSON prompt.

## Current State Analysis

- `Data/ApplicationDbContext.cs` — only Identity tables and DataProtectionKeys; no Plant entity.
- No AI SDK installed; no `OpenAI` NuGet package referenced in `water-me.csproj`.
- `Pages/Plants/Index.cshtml.cs` — placeholder `OnGet()` no-op; plant list is hard-coded empty HTML.
- `Pages/Plants/Index.cshtml` — disabled "Add plant" button with static empty-state copy.
- No `Pages/Plants/Add.*` files.
- `appsettings.Development.json` has a SQLite connection string; no OpenAI config section.
- The app has no application JavaScript beyond Bootstrap CDN; two AJAX interactions will be introduced in this change (AI suggestion + mark-as-watered undo).

## Desired End State

- A signed-in user lands on `/plants` and sees their plant list (or a "no plants yet" empty state) with status badges.
- Clicking "Add plant" navigates to `/plants/add`, where entering a species name and clicking "Get schedule" triggers an AJAX call to OpenAI and populates editable frequency and amount fields inline — no page navigation.
- Saving the form creates a `Plant` row scoped to the current user; the user is redirected to `/plants`.
- Each plant in the list shows one of: "Water today" (null LastWateredAt), "Next watering in N days" (within schedule), or a red "Overdue by N days" badge.
- "Water it" button: AJAX POST records the watering immediately; button switches to "Undo" with a 10-second countdown. Clicking Undo within that window reverts the watering server-side. After 10 seconds, Undo disappears.
- If OpenAI is unavailable or returns unparseable output, the Add Plant page shows an inline error and exposes empty, editable frequency/amount fields for manual entry.
- Full S-02 flow works end-to-end on production (Azure App Service + Azure SQL).

### Key Discoveries

- `Data/ApplicationDbContext.cs:9` — `IdentityDbContext<IdentityUser>` is the base; adding `DbSet<Plant>` here picks up the FK to `AspNetUsers` automatically.
- `Program.cs:27` — `UserManager<IdentityUser>` is in DI; inject into page models to get the current user's ID for query scoping.
- `Program.cs:52-53` — `FallbackPolicy = DefaultPolicy` covers all routes; the Add Plant and Index pages need no `[Authorize]` attribute.
- `Pages/Account/Register.cshtml.cs:8` — establishes the pattern: constructor-injected services, nested `InputModel`, `OnPostAsync` with `ModelState` validation. Add Plant follows the same shape.
- No existing JavaScript logic in the project — two AJAX interactions introduced here are the first; use vanilla `fetch` with the anti-forgery token header to stay framework-free.

## What We're NOT Doing

- No edit-plant or delete-plant UI — that is S-03.
- No watering history table or audit log — `PreviousLastWateredAt` supports the single-level undo only.
- No push/email notifications — FR-010 is parked.
- No photo-based plant identification — plants are added by species name text only.
- No client-side form validation — server-side ModelState validation only, matching S-01 convention.
- No Planta/Greg import or bulk add — one plant at a time for MVP.
- No streaming AI response — a single blocking call with a 5-second timeout.

## Implementation Approach

Five phases in dependency order. Phases 1 and 2 build the foundations (data model + AI service) that Phase 3 (add-plant page) and Phase 4 (list + mark-as-watered) depend on. Phase 5 is the production deployment gate — the change is not done until the full flow works on Azure. Each phase leaves the app in a buildable, runnable state.

## Critical Implementation Details

**User-scoped isolation is a hard security requirement.** Every `Plant` query must include `.Where(p => p.UserId == currentUserId)`. The mark-as-watered and unwater handlers must additionally verify `plant.UserId == currentUserId` before writing — never trust the plant ID alone, as an attacker could submit any ID.

**AJAX anti-forgery pattern.** Razor Pages validates anti-forgery tokens on every POST. For AJAX calls, the JS must read the hidden `__RequestVerificationToken` field already rendered in any `<form>` on the page and pass it as the `RequestVerificationToken` request header. The middleware accepts tokens in either the form body or this header.

**PreviousLastWateredAt enables single-level undo without a history table.** On "Water it": copy `LastWateredAt` → `PreviousLastWateredAt`, set `LastWateredAt = UtcNow`. On "Unwater": restore `LastWateredAt = PreviousLastWateredAt`, clear `PreviousLastWateredAt`. This pattern breaks if the user waters twice in quick succession — acceptable for MVP since the 10-second window prevents that in practice.

**OpenAI API key must never appear in source files.** In development: `dotnet user-secrets set "OpenAI:ApiKey" "sk-..."`. In production: Azure App Service Application Setting `OpenAI__ApiKey`. The `ModelId` (`gpt-56-terra`) lives in `appsettings.json` (non-secret).

**5-second AI timeout.** Wrap the OpenAI SDK call with a `CancellationTokenSource(TimeSpan.FromSeconds(5))`. On `OperationCanceledException` or any exception, return a failure result — never let an AI timeout surface as an unhandled 500.

---

## Phase 1: Plant Entity + EF Migration

### Overview

Define the `Plant` entity, wire it into `ApplicationDbContext`, and generate + apply the EF Core migration. Phase 1 is complete when the database contains a `Plants` table and the app builds cleanly.

### Changes Required

#### 1. Plant model

**File**: `Models/Plant.cs` (new file; create `Models/` folder)

**Intent**: Define the domain entity that holds all per-plant data for S-02. The `UserId` column is the isolation boundary — it must be indexed for query performance. `PreviousLastWateredAt` enables single-level undo without a watering history table.

**Contract**: Class `Plant` in namespace `water_me.Models` with the following properties:
- `Id` (int, primary key)
- `UserId` (string, required) — FK to `AspNetUsers.Id`; not a navigation property for MVP simplicity
- `SpeciesName` (string, required, max 200) — drives the AI lookup
- `Nickname` (string?, nullable, max 200) — user-facing display name; falls back to `SpeciesName` when null
- `WateringFrequencyDays` (int) — how many days between waterings
- `WateringAmount` (string, required, max 200) — free-text description (e.g. "200ml")
- `LastWateredAt` (DateTime?, UTC, nullable) — null means "never watered"
- `PreviousLastWateredAt` (DateTime?, UTC, nullable) — holds the pre-undo value during the 10-second window
- `CreatedAt` (DateTime, UTC) — set at insert time, not exposed in forms

#### 2. ApplicationDbContext update

**File**: `Data/ApplicationDbContext.cs`

**Intent**: Expose `Plants` as a queryable set and configure the index and relationship the entity requires.

**Contract**: Add `DbSet<Plant> Plants` property. In `OnModelCreating`, add an index on `UserId` for query performance: `modelBuilder.Entity<Plant>().HasIndex(p => p.UserId)`. Also set `CreatedAt` as a required column with no default value (the page model sets it explicitly so there's no ambiguity between UTC and local time).

#### 3. EF Core migration

**Command**: `dotnet ef migrations add AddPlant`

**Intent**: Capture the schema delta as a versioned migration that runs in both SQLite (dev) and SQL Server (prod).

**Contract**: The generated migration creates a `Plants` table with all columns from the entity and an index on `UserId`. Verify the generated Up/Down methods before applying. Apply with `dotnet ef database update`.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with Plant model and updated DbContext
- `dotnet ef database update` applies the migration without errors (SQLite dev database)

#### Manual Verification

- `Plants` table exists in `waterme.db` (open with any SQLite browser or run `sqlite3 waterme.db ".tables"`)

**Pause here for manual confirmation before proceeding to Phase 2.**

---

## Phase 2: OpenAI Watering Schedule Service

### Overview

Install the OpenAI NuGet package, define the service interface and implementation (JSON prompt, 5-second timeout, graceful failure), configure appsettings, and register in DI. Phase 2 is complete when the service is injectable and can return a valid suggestion for a real species name.

### Changes Required

#### 1. NuGet package

**Command**: `dotnet add package OpenAI`

**Intent**: Add the official OpenAI .NET SDK so the service can call the Chat Completions API.

#### 2. appsettings.json — OpenAI config section

**File**: `appsettings.json`

**Intent**: Store the non-secret OpenAI configuration (model ID) under a dedicated `OpenAI` key so it can be overridden per environment. The API key is a secret and must not appear in any appsettings file.

**Contract**: Add an `OpenAI` object with one key: `"ModelId": "gpt-56-terra"`. No `ApiKey` field in any appsettings file — set via `dotnet user-secrets` in dev and Azure App Service Application Setting in production.

#### 3. Service interface

**File**: `Services/IWateringScheduleService.cs` (new file; create `Services/` folder)

**Intent**: Define the contract that the Add Plant page model depends on, so the real implementation can be swapped for a test double later.

**Contract**: Interface `IWateringScheduleService` in namespace `water_me.Services` with one method:
```csharp
Task<WateringScheduleResult> GetScheduleAsync(string speciesName, CancellationToken ct = default);
```
Where `WateringScheduleResult` is a record in the same file:
```csharp
record WateringScheduleResult(bool Success, int FrequencyDays, string Amount);
```
On failure, `Success = false` and the numeric/string fields carry zero/empty values.

#### 4. Service implementation

**File**: `Services/OpenAiWateringScheduleService.cs`

**Intent**: Call the OpenAI Chat Completions API with a structured JSON prompt, enforce a 5-second timeout, parse the response, and return a `WateringScheduleResult`. All failures (timeout, network error, parse error, invalid values) return `Success = false` — never throw to the caller.

**Contract**: Class `OpenAiWateringScheduleService : IWateringScheduleService`. Constructor injects `IConfiguration` to read `OpenAI:ApiKey` and `OpenAI:ModelId`. 

System prompt: `"You are a plant care expert. Respond ONLY with a JSON object in this exact format: {\"FrequencyDays\": <int>, \"Amount\": \"<string>\"}. FrequencyDays is the number of days between waterings. Amount is a concise English description of how much water to give (e.g. '200ml' or 'water until it drains from the pot'). Do not include any other text."`

User message: the raw species name string.

Call with a `CancellationTokenSource(TimeSpan.FromSeconds(5))` linked to the passed-in `ct`. Parse the response content as JSON; validate that `FrequencyDays > 0` and `Amount` is non-empty. On any exception or validation failure: log at Warning level and return `new WateringScheduleResult(false, 0, "")`.

#### 5. DI registration

**File**: `Program.cs`

**Intent**: Register the service and expose the result record as a known type to the DI container.

**Contract**: After `builder.Services.AddRazorPages()`, add `builder.Services.AddScoped<IWateringScheduleService, OpenAiWateringScheduleService>()`. The OpenAI SDK manages its own `HttpClient` internally — no `AddHttpClient<T>()` call is needed or correct here.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with the OpenAI package and service files in place

#### Manual Verification

- `dotnet user-secrets set "OpenAI:ApiKey" "sk-..."` configured for the project
- `dotnet run` starts without configuration errors
- Manual smoke test: temporarily call the service from a test endpoint or Razor Page handler, pass `"Monstera deliciosa"`, verify a valid JSON suggestion is returned within 5 seconds

**Pause here for manual confirmation before proceeding to Phase 3.**

---

## Phase 3: Add Plant Page + Inline AI Suggestion

### Overview

Create the `/plants/add` Razor Page. The user enters a species name (and optional nickname), clicks "Get schedule", and the page POSTs to an AJAX handler that calls `IWateringScheduleService` and returns JSON. The JS populates editable frequency and amount fields in place. The user can adjust the values and click "Add plant" to submit the full form and create the plant. If AI fails, the fields are revealed empty for manual entry.

### Changes Required

#### 1. Add Plant page model

**File**: `Pages/Plants/Add.cshtml.cs`

**Intent**: Handle three scenarios on this single page: GET (render empty form), POST suggest (AJAX handler returns JSON), POST save (validate and persist the plant).

**Contract**: Class `AddModel : PageModel` in namespace `water_me.Pages.Plants`. Constructor injects `IWateringScheduleService`, `ApplicationDbContext`, and `UserManager<IdentityUser>`.

`[BindProperty] InputModel Input` with nested class:
- `SpeciesName` (`[Required, StringLength(200)]`)
- `Nickname` (`[StringLength(200)]`)
- `WateringFrequencyDays` (`[Required, Range(1, 365)]`)
- `WateringAmount` (`[Required, StringLength(200)]`)

`OnGetAsync()`: no-op, returns `Page()`.

`OnPostSuggestAsync()`: reads `Input.SpeciesName` from the form body (not bound via full model validation). If `string.IsNullOrWhiteSpace(Input.SpeciesName)`, return `new JsonResult(new { success = false })` immediately — do not call the AI service. Otherwise call `IWateringScheduleService.GetScheduleAsync` and return `new JsonResult(new { success, frequencyDays, amount })`. Does not call `ModelState.IsValid` — this handler only needs the species name.

`OnPostAsync()`: standard save path. If `!ModelState.IsValid` return `Page()`. Build a `Plant` entity: set `UserId = UserManager.GetUserId(User)`, copy all `Input` properties, `CreatedAt = DateTime.UtcNow`. Add to context, save, redirect to `/Plants/Index`.

#### 2. Add Plant page view

**File**: `Pages/Plants/Add.cshtml`

**Intent**: Render the two-stage form: (1) species name + nickname input with a "Get schedule" button that triggers the AJAX call; (2) a hidden section that appears after the suggestion, containing editable frequency and amount fields plus the "Add plant" submit button. Manual fallback: if AI fails, show an inline error message and reveal the fields empty.

**Contract**: Single `<form method="post">` wrapping all inputs. The "Get schedule" button has `type="button"` (not submit) — JS intercepts the click, POSTs to `?handler=Suggest` via `fetch`, and on success populates `#frequency` and `#amount` inputs and shows the `#schedule-section` div. On failure, shows `#ai-error` message and also reveals `#schedule-section` with empty fields.

The `fetch` call:
- URL: `?handler=Suggest`
- Method: POST
- Headers: `{ 'RequestVerificationToken': token }` where `token` is read from `document.querySelector('input[name="__RequestVerificationToken"]').value`
- Body: `FormData` containing the species name field

The `#schedule-section` contains: a label + number input for `Input.WateringFrequencyDays`, a label + text input for `Input.WateringAmount`, and the `type="submit"` "Add plant" button. This section is `display:none` until the AJAX response arrives.

A "Back to my plants" link below the form points to `/plants`.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with Add Plant page in place

#### Manual Verification

- Navigate to `/plants/add` — form renders with species name and nickname fields; schedule section is hidden
- Enter "Monstera deliciosa", click "Get schedule" — within 5 seconds, frequency and amount fields appear, pre-populated with the AI suggestion
- Edit frequency or amount, click "Add plant" — plant saved; redirected to `/plants` (still shows empty state in Phase 3, full list comes in Phase 4)
- Simulate AI failure (disconnect network or set an invalid API key) — inline error appears, empty schedule fields revealed; user can fill in manually and save successfully
- Submit the form with an empty species name — server-side validation error shown
- Submit with `WateringFrequencyDays = 0` — validation error shown

**Pause here for manual confirmation before proceeding to Phase 4.**

---

## Phase 4: Plant List + Mark as Watered

### Overview

Update the `/plants` Index page to query the current user's plants and render the live list with status badges. Enable the "Add plant" button. Add "Water it" / "Undo" AJAX interactions: "Water it" immediately records `LastWateredAt` server-side, switches the button to "Undo" with a 10-second JS countdown; clicking Undo within that window posts to `?handler=Unwater` to restore the previous `LastWateredAt`. Phase 4 is complete when the full S-02 end-to-end flow works locally.

### Changes Required

#### 1. Index page model

**File**: `Pages/Plants/Index.cshtml.cs`

**Intent**: Replace the no-op `OnGet` with a real query that loads the current user's plants, computes their display status, and exposes them to the view. Add POST handlers for water and unwater that enforce user-scope isolation before writing.

**Contract**: Constructor injects `ApplicationDbContext` and `UserManager<IdentityUser>`.

`OnGetAsync()`: query `_db.Plants.Where(p => p.UserId == userId).OrderBy(p => p.CreatedAt).ToListAsync()`. Expose the result as `public IList<Plant> Plants`.

Status helper method `GetStatus(Plant p) → string`:
- `p.LastWateredAt == null` → `"Water today"`
- `daysUntilNext = p.WateringFrequencyDays - (int)(DateTime.UtcNow - p.LastWateredAt.Value).TotalDays`
- `daysUntilNext > 0` → `$"Next watering in {daysUntilNext} day(s)"`
- `daysUntilNext <= 0` → `$"Overdue by {-daysUntilNext} day(s)"`

Expose `GetStatus` as a public method so the view can call it per plant.

`OnPostWaterAsync(int id)`: load plant by ID, verify `plant.UserId == userId` (return `Forbid()` if mismatch), set `PreviousLastWateredAt = LastWateredAt`, `LastWateredAt = DateTime.UtcNow`, save. Return `new JsonResult(new { status = GetStatus(plant) })`.

`OnPostUnwaterAsync(int id)`: same user-scope check, set `LastWateredAt = PreviousLastWateredAt`, `PreviousLastWateredAt = null`, save. Return `new JsonResult(new { status = GetStatus(plant) })`.

#### 2. Index page view

**File**: `Pages/Plants/Index.cshtml`

**Intent**: Render the plant list with status badges, an enabled "Add plant" link, and the AJAX water/undo interaction per row.

**Contract**: Replace the hard-coded empty-state markup. If `Model.Plants.Count == 0`, show the existing "You have no plants yet" copy. Otherwise render a Bootstrap list-group (or table) — one row per plant showing:
- Display name: `plant.Nickname ?? plant.SpeciesName`
- Status badge: Bootstrap `.badge` with `.bg-danger` when status starts with "Overdue", `.bg-warning text-dark` when "Water today", `.bg-success` otherwise
- "Water it" button (`data-plant-id="@plant.Id"`, class `btn-water`)

The "Add plant" button should be an `<a>` link to `/plants/add` styled as `btn btn-primary`.

JS at the bottom of the page:
- On `.btn-water` click: read `data-plant-id`, POST to `?handler=Water` with `{ id }` and the anti-forgery token header; on success (`response.ok`) update the status badge text and CSS class from the JSON response, switch the button text to "Undo" (class `btn-unwater`), start a 10-second countdown shown as "Undo (Ns)"; after 10 seconds revert button text to "Water it" (class `btn-water`). On non-ok response (e.g. 403): log `console.error`, revert button to its pre-click state, leave the badge unchanged.
- On `.btn-unwater` click: POST to `?handler=Unwater` with `{ id }` and token; on success update badge from JSON response, cancel the countdown, revert button to "Water it". On non-ok response: log `console.error`, leave button and badge unchanged.

A single `<form>` with an anti-forgery hidden input is needed anywhere on the page so the JS can read the token value.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with updated Index page

#### Manual Verification

- Sign in and navigate to `/plants` — plant list shows all plants added in Phase 3 testing with correct status badges
- A plant added just now (never watered) shows "Water today" badge
- Click "Water it" — button immediately shows "Undo (10s)" countdown; status badge updates to "Next watering in N days"
- Click "Undo" within 10 seconds — previous status restored; button reverts to "Water it"
- Let the countdown expire (10 seconds) — Undo button disappears; "Water it" button is re-enabled
- Navigate to `/plants` in a private/incognito window (different user) — no plants visible (isolation confirmed)
- `GET /healthz` returns 200 (no regression)

**Pause here for manual confirmation before proceeding to Phase 5.**

---

## Phase 5: Production Deployment

### Overview

Apply the EF Core migration to the Azure SQL production database, configure the OpenAI API key and model ID as Azure App Service Application Settings, deploy the app, and smoke-test the full S-02 flow on production. Phase 5 is complete when the 6-step MVP flow works end-to-end in production.

### Changes Required

#### 1. Azure App Service application settings

**Azure portal / CLI**

**Intent**: Supply the two OpenAI secrets to the production app without checking them into source control.

**Contract**: In Azure App Service → Configuration → Application Settings, add:
- `OpenAI__ApiKey` = `<production OpenAI API key>` (double-underscore maps to nested config)
- `OpenAI__ModelId` = `gpt-56-terra` (or override the appsettings.json default if needed)

#### 2. EF Core migration on Azure SQL

**Intent**: Bring the production Azure SQL schema up to date with the `AddPlant` migration before deploying the new app code.

**Contract**: Run the migration against the production connection string:
```
dotnet ef database update --connection "<azure-sql-connection-string>"
```
Verify via Azure portal or SQL client that the `Plants` table and `IX_Plants_UserId` index exist.

#### 3. Deploy app code

**Intent**: Push the current `main` branch; GitHub Actions CI picks it up and deploys to Azure App Service.

**Contract**: `git push` to `main`. Confirm the GitHub Actions workflow completes successfully. Monitor App Service logs for startup errors.

### Success Criteria

#### Automated Verification

- GitHub Actions CI workflow passes (build + deploy steps succeed)

#### Manual Verification

- Full S-02 flow on production URL:
  1. Register a new account → lands on `/plants`
  2. Click "Add plant" → `/plants/add` renders correctly
  3. Enter a species name → AI suggestion appears within 5 seconds
  4. Edit if desired, click "Add plant" → plant appears in list with "Water today" status
  5. Click "Water it" → countdown appears, status updates to "Next watering in N days"
  6. Wait 10 seconds → countdown expires; "Water it" is re-enabled
- `GET /healthz` returns 200 on production (no regression)
- Verify in Azure SQL that the `Plants` row is associated to the correct user ID (data isolation check)

---

## Testing Strategy

### Manual Testing Steps

1. Register → add "Monstera deliciosa" → AI suggests schedule → accept → plant in list with "Water today".
2. Click "Water it" → badge changes → click "Undo" → badge reverts.
3. Set WateringFrequencyDays to 1 in the DB; wait until tomorrow → verify "Overdue by 1 day(s)" appears.
4. Disconnect network, try to add a plant → AI error shown; fill manually → save succeeds.
5. Open two browser windows with different accounts → each sees only their own plants.
6. Submit Add Plant form with species name over 200 characters → validation error shown.

### Unit Tests

No test project exists yet. When added (per `AGENTS.md`), cover:
- `OpenAiWateringScheduleService`: mock the HTTP call, assert JSON parse success/failure/timeout paths.
- `IndexModel.GetStatus`: assert all three status branches (null, in-range, overdue) with exact string output.
- `IndexModel.OnPostWaterAsync`: assert user-scope check blocks cross-user watering.

## References

- Roadmap S-02: `context/foundation/roadmap.md:76–86`
- PRD FR-003–009, US-01: `context/foundation/prd.md:66–88`
- S-01 plan (account pages pattern): `context/changes/account-flow/plan.md`
- PageModel pattern: `Pages/Account/Register.cshtml.cs`
- Identity + DI: `Program.cs:27–37`
- Cookie auth and FallbackPolicy: `Program.cs:39–53`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Plant Entity + EF Migration

#### Automated

- [x] 1.1 `dotnet build` succeeds with Plant model and updated DbContext
- [x] 1.2 `dotnet ef database update` applies migration without errors

#### Manual

- [x] 1.3 `Plants` table exists in `waterme.db`

### Phase 2: OpenAI Watering Schedule Service

#### Automated

- [ ] 2.1 `dotnet build` succeeds with OpenAI package and service files

#### Manual

- [ ] 2.2 `dotnet run` starts without configuration errors (user-secrets set)
- [ ] 2.3 AI service returns a valid suggestion for a real species name within 5 seconds

### Phase 3: Add Plant Page + Inline AI Suggestion

#### Automated

- [ ] 3.1 `dotnet build` succeeds with Add Plant page

#### Manual

- [ ] 3.2 Species name input → AI suggestion appears inline within 5 seconds
- [ ] 3.3 Edit and save → plant created; redirect to `/plants`
- [ ] 3.4 AI failure → inline error + empty fields for manual entry; save succeeds
- [ ] 3.5 Empty species name or out-of-range frequency → server-side validation error shown

### Phase 4: Plant List + Mark as Watered

#### Automated

- [ ] 4.1 `dotnet build` succeeds with updated Index page

#### Manual

- [ ] 4.2 Plant list shows correct plants with status badges
- [ ] 4.3 "Water today" badge for never-watered plant
- [ ] 4.4 "Water it" click → countdown + status update
- [ ] 4.5 "Undo" within 10 seconds → status reverts
- [ ] 4.6 Countdown expiry → "Water it" re-enabled
- [ ] 4.7 Cross-user isolation: different account sees no plants
- [ ] 4.8 `GET /healthz` returns 200

### Phase 5: Production Deployment

#### Automated

- [ ] 5.1 GitHub Actions CI workflow passes

#### Manual

- [ ] 5.2 Full 6-step S-02 flow works on production URL
- [ ] 5.3 `GET /healthz` returns 200 on production
- [ ] 5.4 Azure SQL `Plants` row is user-scoped (data isolation spot-check)
