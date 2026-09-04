<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Plant Management

- **Plan**: context/changes/plant-management/plan.md
- **Scope**: Phase 1 + Phase 2 of 2 (full plan)
- **Date**: 2026-09-04
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Silent delete failure on Index page (no user feedback)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Pages/Plants/Index.cshtml (delete fetch JS, non-ok branch)
- **Detail**: When the AJAX delete in `Index.cshtml` receives a non-ok response (e.g. 404 because the plant belongs to another user), the JS logs `console.error` and resets the button text — but the user sees no visible error message. The Water/Unwater handlers follow the same silent pattern, but those operations are reversible; a silent delete failure is harder to diagnose and could leave the user confused about whether the plant was deleted.
- **Fix**: In the non-ok / catch branch, after resetting the button, briefly show a small visible error — for example set a temporary error label near the button, or change button text to "Delete failed" for 2 seconds before resetting to "Delete".
- **Decision**: FIXED

### F2 — Delete logic duplicated across Edit and Index page models

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Pages/Plants/Edit.cshtml.cs:91–101, Pages/Plants/Index.cshtml.cs:71–82
- **Detail**: `OnPostDeleteAsync` is implemented identically in both page models (load by id+userId, Remove, SaveChanges, redirect). A future change (soft-delete, audit logging, cascade behaviour) must be applied in both places. The two callers use different invocation patterns — Edit uses a plain form POST with full-page redirect; Index uses AJAX. The two-invocation-pattern split is intentional and acceptable UX, but the shared business logic could live in a shared service method.
- **Fix**: Accept as-is now and document the intentional split. When a test project or service layer is added (per AGENTS.md notes), extract the delete logic into a shared `PlantService.DeleteAsync(int id, string userId)` method.
- **Decision**: ACCEPTED-AS-RULE + FIXED — extracted into PlantService.DeleteAsync; both page models updated to delegate. Rule recorded in context/foundation/lessons.md.

### F3 — Edit-page delete button has no confirmation step

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Pages/Plants/Edit.cshtml:49–51
- **Detail**: The Index page uses a two-click JS confirmation before deleting (first click → "Confirm delete", 3-second timeout). The Edit page delete form is a plain `<form>` POST with no JS confirmation — one accidental click immediately and irreversibly destroys the plant record. The plan did not specify a confirmation on the Edit-page delete, but the UX asymmetry is worth noting.
- **Fix**: Add a small `confirm()` dialog or a data-confirming two-click pattern to the Edit-page delete button, matching the Index-page pattern.
- **Decision**: FIXED — added two-click data-confirming pattern with 3s auto-reset, matching Index-page behaviour.

### F4 — Implicit vs. explicit anti-forgery token on Edit-page delete form

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Pages/Plants/Edit.cshtml:49
- **Detail**: The plan says the delete form should contain "only the anti-forgery token and a Delete button". The implementation relies on ASP.NET Core's automatic token injection (triggered by `asp-page-handler` and `asp-route-id` tag helpers on the `<form>`) rather than an explicit `@Html.AntiForgeryToken()` call. Functionally identical — Razor Pages injects the hidden token automatically — but the implementation differs from the plan's wording. This is a cosmetic plan-language drift, not a security issue.
- **Fix**: No action required. The implicit injection is idiomatic ASP.NET Core and is consistent with how the main save form works. Accept as-is.
- **Decision**: ACCEPTED — implicit anti-forgery injection is idiomatic ASP.NET Core; no code change needed.
