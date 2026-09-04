# Plant Management Implementation Plan

## Overview

Deliver FR-005 and FR-006: edit and delete for the Plant entity. A signed-in user can navigate from their plant list to a dedicated edit page, update any plant field (including species name with optional AI re-suggestion that overwrites the current schedule), and delete the plant from either the edit page or the list. The list gains Edit links per row and two-click inline Delete confirmation.

## Current State Analysis

- No Edit page exists. `Pages/Plants/Add.cshtml.cs` is the established Razor Page pattern to follow.
- `Pages/Plants/Index.cshtml.cs` has Water/Unwater AJAX handlers but no Edit or Delete surface.
- The `Plant` model already covers all editable fields; no schema changes needed.
- `IWateringScheduleService` is injectable; the "Get schedule" AI call is reusable as-is.

## Desired End State

- A user sees "Edit" and "Delete" controls on each row of their plant list.
- Clicking Edit navigates to `/plants/edit/{id}` with all fields pre-populated.
- The edit form includes a "Get schedule" button (species name → overwrites frequency/amount via same AJAX flow as Add).
- Saving redirects to `/plants` with updated values visible in the list.
- Deleting from the edit page or the list (two-click confirm) removes the plant and returns the user to `/plants`.
- All handlers enforce user-scope: a user can only edit/delete their own plants.

### Key Discoveries

- `Pages/Plants/Add.cshtml.cs` — `OnPostSuggestAsync` is the exact handler shape to replicate on the Edit page.
- `Pages/Plants/Index.cshtml` — the existing Water/Unwater JS (fetch + anti-forgery token header pattern) is the template for the inline Delete confirm JS.
- `Models/Plant.cs` — all four editable fields (`SpeciesName`, `Nickname`, `WateringFrequencyDays`, `WateringAmount`) are already present; `LastWateredAt` and `CreatedAt` are not editable.
- `Program.cs:27` — `UserManager<IdentityUser>` is in DI; inject into all new page models.

## What We're NOT Doing

- No edit of `LastWateredAt` or `CreatedAt` — watering history is not editable.
- No bulk delete or multi-select.
- No undo for delete — plants are permanently removed.
- No AI re-suggestion unless the user explicitly clicks "Get schedule" on the edit page.
- No separate confirmation page for delete.
- No re-ordering of the plant list.

## Implementation Approach

Two phases in dependency order. Phase 1 creates the Edit page (new Razor Page following the Add pattern, sharing the AI suggestion AJAX flow, with a Delete handler at the bottom). Phase 2 updates the Index page to surface Edit links and a two-click inline Delete confirm. Both phases leave the app buildable after they complete.

## Critical Implementation Details

**User-scope guard on all new handlers.** Load plants by `(p.Id == id && p.UserId == currentUserId)` — return `NotFound()` if null. Never trust the plant ID from the request body alone.

**Schedule section always visible on Edit.** Unlike the Add page where `#schedule-section` starts hidden, on Edit the schedule fields are pre-populated and always visible. The "Get schedule" JS only needs to overwrite the input values — no show/hide logic.

**Two separate `<form>` elements on the Edit page.** The main save form and the delete form must be separate HTML `<form>` elements so their handler routing doesn't conflict. Both must include the anti-forgery token.

---

## Phase 1: Edit Plant Page

### Overview

Create `/plants/edit/{id}`: a Razor Page that loads the target plant, pre-populates all four editable fields, offers the same "Get schedule" AI AJAX flow as Add, and saves or deletes on POST.

### Changes Required

#### 1. Edit page model

**File**: `Pages/Plants/Edit.cshtml.cs`

**Intent**: Load and update any of the four editable plant fields; reuse the AI suggestion service; enforce user-scope on all writes.

**Contract**: Class `EditModel : PageModel` in namespace `water_me.Pages.Plants`. Constructor injects `ApplicationDbContext`, `UserManager<IdentityUser>`, and `IWateringScheduleService`.

`[BindProperty] InputModel Input` with the same four fields as `AddModel.InputModel`: `SpeciesName` (`[Required, StringLength(200)]`), `Nickname` (`[StringLength(200)]`), `WateringFrequencyDays` (`[Required, Range(1, 365)]`), `WateringAmount` (`[Required, StringLength(200)]`). Additionally expose `public int PlantId { get; private set; }` (non-bound, set from the route in `OnGetAsync`).

`OnGetAsync(int id)`: load plant by `_db.Plants.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId)`. Return `NotFound()` if null. Populate `Input` from the plant and set `PlantId = id`.

`OnPostSuggestAsync()`: identical to `AddModel.OnPostSuggestAsync` — read `Input.SpeciesName`, call `IWateringScheduleService.GetScheduleAsync`, return `new JsonResult(new { success, frequencyDays, amount })`.

`OnPostAsync(int id)`: if `!ModelState.IsValid` return `Page()`. Load plant with user-scope check; overwrite all four fields; `SaveChangesAsync`; redirect to `/Plants/Index`.

`OnPostDeleteAsync(int id)`: load plant with user-scope check; `_db.Plants.Remove(plant)`; `SaveChangesAsync`; redirect to `/Plants/Index`.

#### 2. Edit page view

**File**: `Pages/Plants/Edit.cshtml`

**Intent**: Pre-populated form with all four fields and the "Get schedule" AI flow, plus a separate Delete form at the bottom.

**Contract**: Main `<form method="post" asp-route-id="@Model.PlantId">` wrapping SpeciesName, Nickname, a "Get schedule" `type="button"`, then `#schedule-section` (always visible — no `display:none`) containing WateringFrequencyDays, WateringAmount, and the "Save" `type="submit"` button. The `#ai-error` element starts hidden.

JS is identical to the Add page: on "Get schedule" click, POST species name to `?handler=Suggest` with the anti-forgery token header; on success, set `#frequency` and `#amount` input values directly (no section reveal needed); on failure, show `#ai-error`.

At the bottom, a separate `<form method="post" asp-page-handler="Delete" asp-route-id="@Model.PlantId">` containing only the anti-forgery token and a `type="submit"` button styled `btn btn-danger btn-sm`. A "Back to my plants" link below both forms.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with the new Edit page

#### Manual Verification

- Navigate to `/plants/edit/{id}` — form shows current values pre-populated in all four fields
- Change species name, click "Get schedule" — frequency and amount fields are overwritten with the AI suggestion
- Change any field, click Save — redirected to `/plants`; list shows updated values
- Submit with empty species name or `WateringFrequencyDays = 0` — server-side validation error; no save
- Navigate to `/plants/edit/{id}` logged in as a different user — 404 response
- Click "Delete plant" on the edit page — plant removed, redirected to `/plants`; plant no longer in list

**Pause here for manual confirmation before proceeding to Phase 2.**

---

## Phase 2: Index Page Updates (Edit Links + Inline Delete)

### Overview

Surface Edit and Delete controls on the plant list. Edit is a plain anchor link. Delete uses a two-click JS confirm pattern — first click toggles the button to "Confirm delete", second click POSTs to `?handler=Delete` via fetch. Adds `OnPostDeleteAsync` to the Index page model.

### Changes Required

#### 1. Index page model — Delete handler

**File**: `Pages/Plants/Index.cshtml.cs`

**Intent**: Handle delete requests from the list. Same user-scope guard as all other write handlers.

**Contract**: Add `OnPostDeleteAsync(int id)`: load plant by `(p.Id == id && p.UserId == userId)`, return `NotFound()` if null, `_db.Plants.Remove(plant)`, `SaveChangesAsync`, redirect to `/Plants/Index`.

#### 2. Index page view — Edit link and Delete button per row

**File**: `Pages/Plants/Index.cshtml`

**Intent**: Give each plant row an Edit link and a two-click inline Delete confirm without a page navigation.

**Contract**: Per plant row, after the status badge and "Water it" button, add:
- An `<a href="/plants/edit/@plant.Id">` styled as `btn btn-outline-secondary btn-sm`
- A `<button type="button" class="btn btn-outline-danger btn-sm btn-delete" data-plant-id="@plant.Id">Delete</button>`

JS block (added alongside the water/unwater JS):
- On `.btn-delete` click: if `data-confirming` attribute is absent, set it, change text to "Confirm delete", set a 3-second timeout to reset button text to "Delete" and remove `data-confirming`. If `data-confirming` is present (second click within 3 seconds), clear the timeout, POST to `?handler=Delete` with `{ id: plantId }` and the anti-forgery token header; on `response.ok`, find and remove the closest list row from the DOM. On non-ok response, log `console.error` and reset the button.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with updated Index page

#### Manual Verification

- `/plants` shows an "Edit" link and "Delete" button on every plant row
- Clicking Edit navigates to `/plants/edit/{id}`
- Clicking Delete once changes button text to "Confirm delete"; waiting ~3 seconds resets to "Delete" without deleting
- Clicking "Confirm delete" removes the row from the DOM without page refresh
- Attempting a Delete POST with another user's plant ID returns 404 (verifiable in dev tools)

---

## Testing Strategy

### Manual Testing Steps

1. Add two plants; verify both rows show Edit and Delete controls.
2. Edit plant A: change species name, click "Get schedule", save → updated values visible in list.
3. Edit plant A: change nickname only (no AI call), save → nickname updated.
4. Edit plant A: submit with `WateringFrequencyDays = 0` → validation error, no redirect.
5. Delete plant A via edit page → gone from list.
6. Delete plant B via list (two-click) → row disappears without page reload.
7. Click Delete on list, wait 3 seconds → button resets; plant still in list.

### Unit Tests

No test project yet (per `AGENTS.md`). When added, cover:
- `EditModel.OnGetAsync`: returns `NotFound` when plant belongs to a different user.
- `EditModel.OnPostDeleteAsync`: removes the correct plant and enforces user-scope.
- `IndexModel.OnPostDeleteAsync`: same.

## References

- PRD FR-005, FR-006: `context/foundation/prd.md:71–74`
- Add page pattern (model + view + AI suggestion AJAX): `Pages/Plants/Add.cshtml.cs`, `Pages/Plants/Add.cshtml`
- Water/Unwater AJAX pattern (fetch + anti-forgery token): `Pages/Plants/Index.cshtml`
- Plant entity: `Models/Plant.cs`
- Roadmap S-03: `context/foundation/roadmap.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Edit Plant Page

#### Automated

- [x] 1.1 `dotnet build` succeeds with the new Edit page — 66efa83

#### Manual

- [x] 1.2 Navigate to `/plants/edit/{id}` — form shows current values pre-populated — 66efa83
- [x] 1.3 Change species name, click "Get schedule" — fields overwritten with AI suggestion — 66efa83
- [x] 1.4 Edit any field, click Save — redirected to `/plants`; list shows updated values — 66efa83
- [x] 1.5 Invalid submission — server-side validation error shown; no save — 66efa83
- [x] 1.6 Edit another user's plant — 404 response — 66efa83
- [x] 1.7 Delete from edit page — plant removed, redirected to `/plants` — 66efa83

### Phase 2: Index Page Updates

#### Automated

- [x] 2.1 `dotnet build` succeeds with updated Index page — 7fce3e4

#### Manual

- [x] 2.2 Edit link and Delete button visible on every row — 7fce3e4
- [x] 2.3 Edit link navigates to `/plants/edit/{id}` — 7fce3e4
- [x] 2.4 Delete first click → "Confirm delete"; 3-second timeout resets button — 7fce3e4
- [x] 2.5 Delete second click → row removed from DOM without page refresh — 7fce3e4
- [x] 2.6 Cross-user Delete POST → 404 — 7fce3e4
