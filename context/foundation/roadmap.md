---
project: "Water Me"
version: 1
status: draft
created: 2026-07-22
updated: 2026-08-31
prd_version: 1
main_goal: speed
top_blocker: time
---

# Roadmap: Water Me

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

A casual houseplant collector with 5–20 plants has no reliable way to remember when and how much to water each one. Existing tools (Planta, Greg) are too complex for casual collectors — they require detailed plant profiles, photos, and significant upfront setup. The core product bet — the one trait that, if removed, makes Water Me indistinguishable from a generic plant tracker — is that AI-assisted schedule suggestion removes the setup barrier: the user types a plant name and gets a sensible watering schedule immediately, without any research or manual entry.

## North star

**S-02: user can add a plant, get an AI watering schedule, save it, and mark it as watered** — the north star is the smallest end-to-end slice whose successful delivery proves the core product hypothesis; it is placed as early as prerequisites allow because everything else only matters if this works. This slice is the north star because it covers the full 6-step MVP flow the PRD defines as its primary Success Criterion ("The 6-step MVP flow completes without errors end-to-end"): add plant → AI suggests → user accepts → plant in list with status → mark as watered → countdown resets. Auth and persistence (F-01) and the account flow (S-01) are prerequisites. Plant management (S-03) is clean-up that only matters once this loop is proven.

## At a glance

| ID   | Change ID                 | Outcome (user can …)                                                                                                          | Prerequisites | PRD refs                                    | Status   |
| ---- | ------------------------- | ----------------------------------------------------------------------------------------------------------------------------- | ------------- | ------------------------------------------- | -------- |
| F-01 | persistence-auth-scaffold | (foundation) EF Core + Identity wired; DB migration runs; auth middleware active                                              | —             | NFR (data integrity, isolation), FR-001, FR-002, Access Control | done     |
| S-01 | account-flow              | sign up, sign in, sign out, and reach their empty plant list                                                                  | F-01          | FR-001, FR-002                              | proposed |
| S-02 | core-loop                 | add a plant by species name, get an AI watering schedule, save it, see it in their list with status, and mark it as watered  | S-01          | FR-003, FR-004, FR-007, FR-008, FR-009, US-01 | proposed |
| S-03 | plant-management          | edit a plant's watering info and delete a plant from their list                                                               | S-02          | FR-005, FR-006                              | proposed |

## Baseline

What's already in place in the codebase as of 2026-07-22 (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** absent — no UI framework, Views, Pages, or wwwroot; project is a bare ASP.NET Core 9.0 API shell (`Program.cs`)
- **Backend / API:** partial — WebApplication + OpenAPI registered (`Program.cs:3,5`); only a demo `GET /weatherforecast` endpoint; no domain routes
- **Data:** absent — no EF Core, DbContext, or migrations; demo uses in-memory arrays
- **Auth:** absent — no Identity, JWT, session, or `[Authorize]` middleware
- **Deploy / infra:** partial — `.github/workflows/deploy.yml` deploys to Azure App Service via OIDC; no Dockerfile; no Bicep/ARM templates
- **Observability:** partial — built-in ILogger + basic LogLevel config (`appsettings.json`); health-check endpoint registered; no Serilog, App Insights, or OTel

## Foundations

### F-01: Persistence + auth infrastructure

- **Outcome:** (foundation) EF Core wired with ApplicationDbContext (including ASP.NET Core Identity tables), database connection strings configured per-environment (SQLite for dev, Azure SQL for prod), initial migration applied, Identity services registered, auth and authorization middleware in the pipeline, cookie session scheme configured, unauthenticated requests redirected to sign-in.
- **Change ID:** persistence-auth-scaffold
- **PRD refs:** NFR (data integrity guardrail: "plant list and watering history must never be lost"; data isolation: "never accessible to any other user account"), FR-001, FR-002, Access Control section
- **Unlocks:** S-01 (sign-up/sign-in pages need Identity services and a running DB), S-02 (plant data and watering events need a user-scoped DbContext), S-03 (edit/delete need persistence)
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Merges EF Core and Identity in one step because ASP.NET Core Identity requires a DbContext — splitting them produces a non-functional intermediate state. If the Azure SQL connection string is not available at dev time, SQLite lets the Foundation land without blocking S-01.
- **Status:** done

## Slices

### S-01: Account flow

- **Outcome:** user can sign up with email and password, sign in, sign out, and reach their empty plant list page
- **Change ID:** account-flow
- **PRD refs:** FR-001, FR-002
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sign-up friction before the user sees any product value; mitigated by keeping the form minimal (email + password only, no profile fields at sign-up).
- **Status:** proposed

### S-02: Core MVP loop ★ north star

- **Outcome:** user can add a plant by entering its species name, see an AI-suggested watering schedule (frequency + amount), edit it if needed, save it, see the plant appear in their list with a "next watering in N days" status, mark it as watered, and see the countdown reset immediately; if AI is unavailable or the species is not recognised, the user sees an error state and can enter the schedule manually
- **Change ID:** core-loop
- **PRD refs:** FR-003, FR-004, FR-007, FR-008, FR-009, US-01
- **Prerequisites:** S-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Spans two distinct user interactions (add-plant flow and mark-as-watered action) — cohesive because both touch the same Plant entity and list page, and the PRD defines them as one 6-step flow. Three NFR risks: (1) AI suggestion must arrive within 5 seconds — streaming or timeout strategy required; (2) plant list must remain usable when AI is down — graceful degradation; (3) every plant query must be user-scoped — an isolation bug is a product-failure-level data breach. Mark-as-watered needs a ~10-second undo toast to satisfy the data integrity guardrail.
- **Status:** proposed

### S-03: Plant management (edit + delete)

- **Outcome:** user can edit a plant's watering schedule (frequency and amount) after initial setup, and delete a plant from their list
- **Change ID:** plant-management
- **PRD refs:** FR-005, FR-006
- **Prerequisites:** S-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Edit and delete share UI surface (same plant detail/edit page); treating them as one slice avoids splitting a single-screen interaction across two changes.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                 | Suggested issue title                             | Ready for `/10x-plan` | Notes                                     |
| ---------- | ------------------------- | ------------------------------------------------- | --------------------- | ----------------------------------------- |
| F-01       | persistence-auth-scaffold | Wire up EF Core + ASP.NET Core Identity           | yes                   | Run `/10x-plan persistence-auth-scaffold` |
| S-01       | account-flow              | Sign-up, sign-in, sign-out, and empty plant list  | no                    | Depends on F-01                           |
| S-02       | core-loop                 | Full 6-step MVP loop (add + AI + water)           | no                    | Depends on S-01; AI provider: OpenAI      |
| S-03       | plant-management          | Edit and delete plants                            | no                    | Depends on S-02                           |

## Open Roadmap Questions

No open questions — all resolved.

## Parked

- **FR-010: Notifications (push/email reminders when a plant is due for watering)** — Why parked: PRD §Notifications marks as nice-to-have; MVP proves the core loop first; notification-driven retention is a v1.1 concern.
- **Photo-based plant identification** — Why parked: PRD §Non-Goals; plants are added by species name only for MVP.
- **Fertilization, repotting, or other care types** — Why parked: PRD §Non-Goals; watering schedule is the only domain rule in scope for MVP.
- **Seasonal or temperature adjustments to watering schedules** — Why parked: PRD §Non-Goals; schedules are fixed after setup for MVP.
- **Mobile native app (iOS/Android)** — Why parked: PRD §Non-Goals; web browser is the only delivery channel for MVP.

## Done

- **F-01: (foundation) EF Core + Identity wired; DB migration runs; auth middleware active** — Archived 2026-08-31 → `context/archive/2026-08-10-persistence-auth-scaffold/`. Lesson: —.
