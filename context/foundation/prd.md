---
project: "Water Me"
version: 1
status: draft
created: 2026-06-11
context_type: greenfield
product_type: web-app
target_scale:
  users: small
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
---

## Vision & Problem Statement

A casual houseplant collector with 5–20 plants has no reliable way to remember when and how much to water each one. The moment the pain surfaces is standing in front of their plants with no memory of the last watering or the schedule. Without a system, they guess or inspect the soil — and plants die or struggle as a result.

Existing tools (Planta, Greg) are too complex for casual collectors: they require detailed plant profiles, photos, and significant upfront setup. The insight is that AI-assisted schedule suggestion with one-tap acceptance removes the setup barrier — the user types a plant name and gets a sensible schedule immediately.

## User & Persona

**Primary persona: The Casual Collector**
A person who owns 5–20 houseplants and cares about keeping them alive, but is not a dedicated plant hobbyist. They don't want to research watering schedules or maintain a detailed care log. They reach for this product when they realize they can't remember when they last watered a specific plant — or when they've just brought a new plant home and don't know its needs.

## Success Criteria

MVP flow: sign-up → add plant by species name → AI retrieves watering info → user reviews/edits/accepts → plant list with status → user marks plant as watered.

### Primary
- The 6-step MVP flow completes without errors end-to-end.
- At least 75% of AI-generated watering suggestions are accepted by users without editing.

### Secondary
- Users return to mark plants as watered at least once per week (retention signal: the app is being used habitually, not just set up and abandoned).

### Guardrails
- A user's plant list and watering history must never be lost (data integrity failure = product failure regardless of AI quality).
- The AI suggestion must never display nonsensical or harmful watering information (a confidently wrong schedule destroys user trust immediately and is unrecoverable).

## User Stories

### US-01: User adds a plant and accepts AI watering schedule

- **Given** a logged-in user on their plant list (empty or with existing plants)
- **When** they enter a species name and submit
- **Then** the app displays an AI-suggested watering schedule (frequency and amount) for that species

#### Acceptance Criteria
- The suggestion appears without requiring the user to leave the page
- The user can edit frequency and/or amount before accepting
- After accepting, the plant appears in the list alongside any existing plants, each showing its current watering status ("next watering: in N days" calculated from today)
- If the AI cannot find information for the species name entered, the user sees an error state and can edit the name or enter the schedule manually

## Functional Requirements

### Authentication
- FR-001: User can create an account with email and password. Priority: must-have
  > Socrates: Counter-argument considered: sign-up friction kills conversion before users see any value. Resolution: kept; accounts are required to persist the plant list across devices. Local-only storage was ruled out in Phase 2.
- FR-002: User can sign in with email and password. Priority: must-have
  > Socrates: Same counter-argument as FR-001. Resolution: kept with FR-001.

### Plant management
- FR-003: User can add a plant to their list by entering its species name. Priority: must-have
  > Socrates: Counter-argument considered: species names are inconsistent (common names, Latin names, misspellings) — AI lookup may fail silently. Resolution: kept; failure is handled gracefully via fallback to manual schedule entry (already captured in US-01 acceptance criteria).
- FR-004: User can view their full plant list with each plant's current watering status. Priority: must-have
  > Socrates: Counter-argument considered: status is meaningless before the first mark-as-watered event. Resolution: kept; new plants show an explicit "not yet watered" state until the first watering action is recorded.
- FR-005: User can edit a plant's watering information after initial setup. Priority: must-have
  > Socrates: Counter-argument considered: edit adds UI surface area and edge cases disproportionate to MVP value. Resolution: kept; a list with no edit is broken from first use — users will make mistakes.
- FR-006: User can remove a plant from their list. Priority: must-have
  > Socrates: Same counter-argument as FR-005. Resolution: kept with FR-005.

### AI-assisted setup
- FR-007: User can view an AI-suggested watering schedule for a plant based on its species name. Priority: must-have
  > Socrates: Counter-argument considered (as part of FR-007–009 group): separate edit and accept steps add friction — saving should equal acceptance. Resolution: FR-008 and former FR-009 merged; see FR-008.
- FR-008: User can save a watering schedule (pre-filled by AI, editable) to their plant's profile. Saving IS acceptance. Priority: must-have
  > Socrates: Merged with former FR-009. Edit + accept collapsed into one save action — user edits if needed, saves, done. The 75% acceptance metric is measured by tracking whether the user edited before saving.

### Watering tracking
- FR-009: User can mark a plant as watered, recording the date. Priority: must-have
  > Socrates: Counter-argument considered: accidental mark-as-watered resets the countdown with no undo. Resolution: kept; a short undo window (e.g. toast with "Undo" for ~10 seconds) after marking covers the mistake case at low dev cost.

### Notifications
- FR-010: User can receive reminders when a plant is due for watering. Priority: nice-to-have
  > Socrates: Counter-argument considered: without notifications the retention criterion (users return weekly) may be impossible to hit. Resolution: kept as nice-to-have; MVP proves the core loop and value proposition first. Notification-driven retention is a v1.1 concern.

## Non-Functional Requirements

- An AI-generated watering suggestion is delivered within 5 seconds of the user submitting a species name (user-perceived latency cap; longer feels broken).
- A user's plant list and watering history are never accessible to any other user account (strict data isolation; flat user model makes this an absolute boundary).
- The app remains usable for existing plants and schedules when the AI service is temporarily unavailable; only the AI-assisted add-plant flow is blocked during the outage.

## Business Logic

The app derives a watering schedule for each plant from its species (via AI) and tracks adherence over time, always knowing and showing when each plant is next due.

Inputs the rule consumes: the species name entered by the user (drives the AI lookup); the watering schedule (frequency + amount) — AI-suggested and user-editable; and the last-watered date recorded each time the user marks a plant as watered.

Output: a "next watering in N days" status per plant, recalculated after every mark-as-watered event.

How the user encounters it: during plant setup (AI fills in the schedule; user reviews and saves); on the plant list (every plant shows its current watering status at a glance); and after marking as watered (the countdown resets immediately).

## Access Control

Multi-user web app. Authentication: email + password (sign-up and sign-in). Flat user model — each account is fully isolated; a user sees only their own plants. No roles, no admin view, no shared plant lists. An unauthenticated user hitting a gated route is redirected to sign-in.

## Non-Goals

- No photo-based plant identification — plants are added by species name only; camera/image recognition is out of scope for MVP.
- No fertilization, repotting, or other care types — the domain rule covers watering schedule only; other care dimensions are a separate product concern.
- No seasonal or temperature adjustments to watering schedules — schedules are fixed after setup; environmental factors are not factored in for MVP.
- No mobile native app — web browser is the only delivery channel; iOS/Android apps are explicitly deferred.

## Open Questions

No outstanding questions — all shape-notes quality checks passed with `quality_check_status: accepted`.
