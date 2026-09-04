# Plant Management — Plan Brief

> Full plan: `context/changes/plant-management/plan.md`

## What & Why

Add edit and delete capabilities for plants (FR-005, FR-006). Without these, the app is broken from first real use — a misspelled species name or a wrong watering schedule has no recovery path short of deleting via the database. This slice closes the minimum viable loop started in S-02.

## Starting Point

S-02 (core-loop) delivered Add Plant, the plant list with status badges, and mark-as-watered. No Edit or Delete surface exists. The `Plant` model already holds all needed fields; no schema changes are required.

## Desired End State

Each plant row on `/plants` has an Edit link and a two-click Delete button. The Edit page (`/plants/edit/{id}`) pre-populates all fields, offers the same AI schedule suggestion as the Add page (overwriting current values if used), and saves with a redirect back to the list. Delete works from both the edit page (one click) and the list (two-click inline confirm, no page navigation).

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Editable fields | All four (SpeciesName, Nickname, Freq, Amount) | Users make typos; locking species forces delete-and-re-add | Plan |
| AI on edit | "Get schedule" button present, overwrites schedule | Identical flow to Add — zero new JS patterns | Plan |
| Delete confirmation (list) | Two-click inline JS confirm (3-second reset) | No page navigation; follows the Water/Unwater AJAX pattern | Plan |
| Delete confirmation (edit) | Single submit button at page bottom | User navigated deliberately; extra confirm adds no safety | Plan |
| Edit layout | Separate `/plants/edit/{id}` Razor Page | Consistent with Add pattern; no new UI paradigms | Plan |
| Post-edit redirect | Back to `/plants` | Matches Add behavior; user sees updated plant in context | Plan |
| Schedule section on Edit | Always visible (no hide/reveal) | Values are pre-populated; only value-overwrite JS needed | Plan |

## Scope

**In scope:**
- New `Pages/Plants/Edit.cshtml` + `Edit.cshtml.cs` with save and delete handlers
- "Get schedule" AI suggestion button on Edit page (overwrite-on-use)
- `OnPostDeleteAsync` on Index page model
- Edit link + Delete two-click confirm per row on the plant list

**Out of scope:**
- Editing `LastWateredAt` or `CreatedAt`
- Bulk delete or multi-select
- Undo for delete
- Schema / migration changes

## Architecture / Approach

Two new handlers on `EditModel` (save + delete) mirror the pattern in `AddModel`. The AI suggestion reuses `IWateringScheduleService` unchanged. Delete from the list is an AJAX POST to `Index?handler=Delete`, same fetch + anti-forgery pattern as Water/Unwater. Both delete paths enforce the same user-scope guard: `p.Id == id && p.UserId == currentUserId`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Edit Plant Page | `/plants/edit/{id}` — full edit + delete from page | Schedule section always-visible differs from Add; JS must not hide it |
| 2. Index Page Updates | Edit links + two-click inline delete on list | DOM removal after delete must target the correct row |

**Prerequisites:** S-02 (core-loop) archived — Plant entity, Add page, and Index AJAX pattern all in place.
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- If a plant is being watered (undo window active) when the user edits it, `PreviousLastWateredAt` is untouched by the edit — acceptable since undo only depends on that field, not on schedule fields.
- The Delete form on the Edit page is a separate `<form>` element from the save form — must be kept separate to avoid handler routing conflicts.

## Success Criteria (Summary)

- User can edit any plant field, optionally re-run AI suggestion, save, and see updated values in the list.
- User can delete a plant from the edit page or the list; the plant disappears immediately.
- A user cannot edit or delete another user's plant (user-scope guard returns 404).
