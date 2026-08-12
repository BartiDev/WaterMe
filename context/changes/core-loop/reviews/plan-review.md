<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Core MVP Loop Implementation Plan

- **Plan**: `context/changes/core-loop/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-12
- **Verdict**: SOUND (after fixes)
- **Findings**: 0 critical | 1 warning | 2 observations

## Verdicts

| Dimension | Verdict |
|---|---|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | WARNING |
| Plan Completeness | PASS |

## Grounding

5/5 existing paths verified (Data/ApplicationDbContext.cs, Pages/Plants/Index.cshtml.cs, Pages/Plants/Index.cshtml, appsettings.json, Program.cs), symbols verified (IdentityDbContext, UserManager, FallbackPolicy), brief↔plan consistent, contract-surfaces.md absent (skipped).

## Findings

### F1 — AddHttpClient<T>() is dead code / contradicts constructor contract

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 — DI registration (Program.cs)
- **Detail**: Plan called `AddHttpClient<OpenAiWateringScheduleService>()` + `AddScoped<IWateringScheduleService, OpenAiWateringScheduleService>()`. The service constructor takes only `IConfiguration`. `AddHttpClient<T>()` expects a `HttpClient`-accepting constructor; the call was dead code and misleading.
- **Fix Applied**: Removed `AddHttpClient<>()` call. Plan now specifies only `AddScoped<IWateringScheduleService, OpenAiWateringScheduleService>()`.
- **Decision**: FIXED (Fix A)

### F2 — OnPostSuggestAsync calls AI with no guard on empty SpeciesName

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 3 — Add.cshtml.cs
- **Detail**: Suggest handler skipped `ModelState.IsValid` (correct) but didn't guard against null/whitespace `SpeciesName`, which would waste an API call and return a nonsense response.
- **Fix Applied**: Added early return `{ success = false }` if `string.IsNullOrWhiteSpace(Input.SpeciesName)` before calling the AI service.
- **Decision**: FIXED

### F3 — Water/Unwater AJAX handlers return Forbid() but JS expected JSON only

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 4 — Index.cshtml.cs + Index.cshtml JS
- **Detail**: On a cross-user attempt the handler returns 403. The JS only described the success path; a 403 would leave the button stuck in "Undo" mode with the countdown running.
- **Fix Applied**: Added non-ok response branch to JS contract: revert button and badge to pre-click state, log `console.error`.
- **Decision**: FIXED
