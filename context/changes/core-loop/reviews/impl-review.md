<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Core MVP Loop Implementation Plan

- **Plan**: context/changes/core-loop/plan.md
- **Scope**: Phases 1–4 of 5 (all completed phases; Phase 5 production deployment pending)
- **Date**: 2026-09-01
- **Verdict**: REJECTED
- **Findings**: 1 critical, 5 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Authorization leak + NullReferenceException in Water/Unwater handlers

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Pages/Plants/Index.cshtml.cs:46, 60
- **Detail**: `FindAsync(id)` returns `null` if the plant doesn't exist — the next line `plant.UserId != userId` then throws `NullReferenceException`, producing an unhandled 500. For plants that do exist but belong to another user, `Forbid()` returns HTTP 403, while a missing plant (after the crash is fixed with a null check) would return 404 — this distinction lets an attacker enumerate valid plant IDs belonging to other users by observing 403 vs 404. The plan's Critical Implementation Details explicitly require user-scope verification before writing.
- **Fix**: Replace `FindAsync(id)` with a single query that incorporates the user scope, and return `NotFound()` in all non-owner branches:
  ```csharp
  var plant = await _db.Plants
      .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
  if (plant == null) return NotFound();
  ```
  Apply to both `OnPostWaterAsync` and `OnPostUnwaterAsync`. This eliminates both the NullReferenceException and the information leak in one change.
  - Strength: Matches the pattern implied by the plan ("verify plant.UserId == currentUserId before writing — never trust the plant ID alone"); collapses two failure modes into one safe response.
  - Tradeoff: Minimal — two near-identical one-query changes.
  - Confidence: HIGH — the plan's security requirement is explicit.
  - Blind spot: None significant.
- **Decision**: FIXED

### F2 — HttpClient re-instantiated on every AI call

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Services/OpenRouterWateringScheduleService.cs:40-41
- **Detail**: `OpenAIClient` and `ChatClient` are constructed inside `GetScheduleAsync` on every invocation. `OpenAIClient` internally creates an `HttpClient` per instance. The service is `Scoped` (new instance per request), so each AI call opens a new socket. Under concurrent load this causes socket exhaustion — the classic `HttpClient` misuse problem. This is not visible in single-user testing but becomes a production reliability risk.
- **Fix**: Move `OpenAIClient` and `ChatClient` construction into the constructor and store as private fields. Each scoped instance then holds exactly one HTTP connection for its lifetime.
  - Strength: Follows the standard .NET guidance for HttpClient reuse; eliminates socket exhaustion risk without requiring a more complex `IHttpClientFactory` setup.
  - Tradeoff: Minor constructor growth; slightly harder to unit-test without a real API key (but that was already the case).
  - Confidence: HIGH — well-documented .NET pattern.
  - Blind spot: If `OpenAIClient` internally uses `IHttpClientFactory` already (check SDK internals), the risk may be lower — but constructing per-call is still wasteful.
- **Decision**: FIXED

### F3 — AI timeout is 15 s (plan specifies 5 s)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Services/OpenRouterWateringScheduleService.cs:36
- **Detail**: `cts.CancelAfter(TimeSpan.FromSeconds(15))`. The plan's "What We're NOT Doing" and Critical Implementation Details both state a **5-second timeout** for the blocking AI call. A 15-second hang degrades the Add Plant UX — the user stares at a spinner for up to 15 seconds before seeing an error or a result.
- **Fix**: Change `TimeSpan.FromSeconds(15)` to `TimeSpan.FromSeconds(5)`.
- **Decision**: FIXED

### F4 — Null-forgiving operator on GetUserId suppresses auth misconfiguration

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Pages/Plants/Add.cshtml.cs:65, Pages/Plants/Index.cshtml.cs:24, 44, 59
- **Detail**: `_userManager.GetUserId(User)!` uses the null-forgiving operator. `GetUserId` returns `null` when the user claim is absent. The global `FallbackPolicy = DefaultPolicy` (Program.cs) makes this safe in normal operation, but if any page ever adds `[AllowAnonymous]` or the auth middleware order shifts, `null` is silently passed as `UserId`, creating orphaned Plant rows with `UserId = null` that would surface to the wrong queries or cause data leakage.
- **Fix**: Replace `!` with an explicit guard: `?? throw new InvalidOperationException("Authenticated user has no ID claim.")`. Apply to all four call sites.
- **Decision**: FIXED

### F5 — CSRF token JS selector has no null guard

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Pages/Plants/Index.cshtml:53
- **Detail**: `document.querySelector('#csrf-form input[name="__RequestVerificationToken"]').value` — no null check before `.value`. If the hidden form is accidentally removed in a future layout edit, this throws `TypeError: Cannot read properties of null`, silently breaking all Water/Unwater buttons with no visible error to the user.
- **Fix**: Add a guard: `const tokenEl = document.querySelector('#csrf-form input[name="__RequestVerificationToken"]'); if (!tokenEl) { console.error('CSRF token element missing'); return; } const token = tokenEl.value;`
- **Decision**: FIXED

### F6 — Missing AsNoTracking on read-only plant list query

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Pages/Plants/Index.cshtml.cs:25-28
- **Detail**: `OnGetAsync` loads the plant list for display only — it never calls `SaveChangesAsync`. Without `.AsNoTracking()`, EF Core attaches all returned entities to the change tracker, wasting memory and CPU proportional to the number of plants per page load.
- **Fix**: Add `.AsNoTracking()` before `.ToListAsync()` in the plant list query.
- **Decision**: FIXED

### F7 — _Layout.cshtml changed without a plan entry

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: Pages/Shared/_Layout.cshtml
- **Detail**: The shared layout was updated (Bootstrap CDN, navbar with brand link, authenticated user display, sign-in/register nav links) — not listed in the plan's "Changes Required". The change is justified and necessary for the plant pages to render in a consistent shell, but it is untracked scope.
- **Fix**: Accept as justified scope — the plan implicitly required these for the plant pages to be usable. No code change needed; optionally add a note to the plan for traceability.
- **Decision**: FIXED — addendum added to plan Phase 4

### F8 — Range(1,365) validation not on domain entity

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Models/Plant.cs:18
- **Detail**: `WateringFrequencyDays` has `[Range(1, 365)]` only on `InputModel` (Add.cshtml.cs). The domain entity carries no such constraint, so plants created outside the form path (DB seeding, admin tools, future API) can have out-of-range values that produce nonsense output from `GetStatus()`.
- **Fix**: Add `[Range(1, 365)]` to `Plant.WateringFrequencyDays` in `Models/Plant.cs`.
- **Decision**: FIXED

### F9 — OnGet instead of OnGetAsync on Add page

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Pages/Plants/Add.cshtml.cs:47
- **Detail**: Plan specifies `OnGetAsync()`. Implementation uses synchronous `OnGet()`. Functionally identical (no async work on GET), but diverges from the plan and from the async-throughout convention used in `IndexModel`.
- **Fix**: Rename to `OnGetAsync` and return `Task<IActionResult>` to match the plan and the pattern in Index.cshtml.cs.
- **Decision**: FIXED

### F10 — Unbounded plant list (no pagination)

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Pages/Plants/Index.cshtml.cs:25-28
- **Detail**: The plant list query returns all plants for the user with no `Take(N)` cap. Acceptable for MVP; becomes a memory and render issue if a user adds hundreds of plants.
- **Fix**: Track as a backlog item. Add cursor- or offset-based pagination with a `Take(50)` cap when S-03 scope warrants it.
- **Decision**: SKIPPED — acceptable for MVP; revisit pagination in S-03
