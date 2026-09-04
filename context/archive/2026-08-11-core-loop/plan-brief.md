# Core MVP Loop — Plan Brief

> Full plan: `context/changes/core-loop/plan.md`

## What & Why

Deliver S-02 — the 6-step flow that proves the core product hypothesis: a signed-in user adds a plant by species name, gets an AI-suggested watering schedule in place (no page change), saves it, sees the plant in their list with a live countdown status, marks it as watered, and can undo within 10 seconds. This is the north-star slice: if this works, the app's core value proposition is proven.

## Starting Point

The S-01 account flow is complete: sign-up, sign-in, sign-out, and the `/plants` placeholder are in place. `ApplicationDbContext` has no domain tables yet — only Identity and DataProtectionKeys. No AI SDK is installed.

## Desired End State

An authenticated user can add a plant, get an OpenAI-suggested watering schedule inline (or enter one manually if AI is unavailable), see all their plants in a list with status badges ("Water today", "Next watering in N days", or a red "Overdue by N days"), mark any plant as watered, and undo the action within a 10-second window. The full flow works on production (Azure App Service + Azure SQL).

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| AI provider | OpenAI (`gpt-56-terra`) | User-specified model; configured via appsettings (model ID) + user-secrets/env var (API key). | Plan |
| Add-plant UX | AJAX fetch (vanilla JS) | PRD requires suggestion without leaving the page; ~30 lines of vanilla JS avoids adding a framework to an otherwise server-side app. | Plan |
| Watering amount | Free-text string | Flexible for AI phrasing ("water until it drains from the pot"), no parsing complexity, sufficient for MVP. | Plan |
| Undo model | Immediate POST + 10s server-side undo | Button changes to "Undo" immediately after click; second POST restores `PreviousLastWateredAt`; no silent data loss if page closes. | Plan |
| Never-watered status | "Water today" | Treat `LastWateredAt = null` as due immediately — simple rule, pushes user to act. | Plan |
| Overdue display | "Overdue by N days" (red badge) | Clear and actionable; distinct from healthy status via Bootstrap `.bg-danger`. | Plan |
| AI failure UX | Inline error + empty editable fields | Satisfies PRD NFR ("app remains usable when AI is unavailable"); no dead ends. | Plan |
| Duplicates | Allowed | Same species in two pots is a real use case; no deduplication logic needed. | Plan |
| Plant name | Nickname (optional) + SpeciesName | Nickname solves the duplicate-readability problem; SpeciesName drives AI lookup independently. | Plan |
| API key storage | `dotnet user-secrets` (dev), Azure App Service setting (prod) | Never in source files; standard .NET secret management pattern. | Plan |
| Production scope | Included as Phase 5 | Change is not done until the full flow works on Azure SQL; Phase 5 catches migration drift early. | Plan |

## Scope

**In scope:** Plant entity + EF migration, OpenAI service with timeout and fallback, Add Plant page (AJAX suggestion + manual fallback), plant list with live status badges, mark-as-watered with 10-second undo, production deployment.

**Out of scope:** Edit/delete plant (S-03), watering history / audit log, push notifications (FR-010), photo-based identification, client-side JS validation, streaming AI response.

## Architecture / Approach

Three new layers added to the existing Razor Pages app:
1. **Data** — `Models/Plant.cs` + EF migration (one table, `UserId` index for isolation).
2. **Service** — `Services/IWateringScheduleService` / `OpenAiWateringScheduleService` (OpenAI SDK, JSON prompt, 5s timeout, graceful failure).
3. **UI** — `Pages/Plants/Add` (two-phase form + fetch handler) and updated `Pages/Plants/Index` (query + status logic + water/unwater AJAX handlers).

All plant queries and write operations are scoped to `UserManager.GetUserId(User)` — isolation is enforced at the data-access layer, not just the UI.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Plant Entity + EF Migration | `Plants` table in dev DB; builds cleanly | Migration shape differs between SQLite (dev) and SQL Server (prod) — verify column types |
| 2. OpenAI Service | Injectable AI service; API key wired via user-secrets | `gpt-56-terra` model availability; 5-second timeout may be tight under load |
| 3. Add Plant Page | Inline AI suggestion via AJAX; manual fallback | Anti-forgery token handling in fetch; JS state management for show/hide sections |
| 4. Plant List + Mark as Watered | Full local S-02 flow; undo working | `PreviousLastWateredAt` concurrent-edit edge case (acceptable for MVP) |
| 5. Production Deployment | End-to-end flow on Azure | EF migration against live Azure SQL; secret config in App Service |

**Prerequisites:** S-01 fully merged to `main` (done — `85cbeaa`). OpenAI API key obtained for the `gpt-56-terra` model.
**Estimated effort:** ~3–5 sessions across 5 phases.

## Open Risks & Assumptions

- `gpt-56-terra` is assumed to support the Chat Completions API with JSON-only system prompt instructions — verify before Phase 2 manual testing.
- The 5-second AI latency budget may be tight on the first cold call after a deployment; consider whether the timeout should be slightly longer (e.g., 8s) if cold-start latency is observed.
- `PreviousLastWateredAt` single-level undo breaks if a user marks the same plant as watered twice within the 10-second window (acceptable for MVP; no safeguard planned).

## Success Criteria (Summary)

- The 6-step MVP flow completes without errors end-to-end on production: sign-up → add plant → AI schedule → save → list with status → mark as watered → countdown resets.
- A plant belonging to User A is never visible to User B (data isolation spot-checked on production).
- If OpenAI is unavailable, the add-plant flow degrades gracefully to manual entry — no 500 errors.
