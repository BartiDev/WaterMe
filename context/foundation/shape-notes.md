---
project: "Water Me"
context_type: greenfield
created: 2026-06-10
updated: 2026-06-10
checkpoint:
  current_phase: 3
  phases_completed: [1, 2]
  gray_areas_resolved:
    - topic: "pain category"
      decision: "missing capability — no good simple tool exists for casual collectors"
    - topic: "primary persona"
      decision: "casual houseplant collector with 5–20 plants who forgets watering schedules"
    - topic: "cost today"
      decision: "user guesses or checks soil; plants sometimes die as a result"
    - topic: "insight"
      decision: "existing apps too complex/heavy; AI suggestion + one-tap accept lowers the barrier"
    - topic: "auth method"
      decision: "email + password login"
    - topic: "role model"
      decision: "flat — each user sees only their own plants; no role separation"
  frs_drafted: 0
  quality_check_status: pending
---

## Vision & Problem Statement

A casual houseplant collector with 5–20 plants has no reliable way to remember when and how much to water each one. The moment the pain surfaces is standing in front of their plants with no memory of the last watering or the schedule. Without a system, they guess or inspect the soil — and plants die or struggle as a result.

Existing tools (Planta, Greg) are too complex for casual collectors: they require detailed plant profiles, photos, and significant upfront setup. The insight is that AI-assisted schedule suggestion with one-tap acceptance removes the setup barrier — the user types a plant name and gets a sensible schedule immediately.

## User & Persona

**Primary persona: The Casual Collector**
A person who owns 5–20 houseplants and cares about keeping them alive, but is not a dedicated plant hobbyist. They don't want to research watering schedules or maintain a detailed care log. They reach for this product when they realize they can't remember when they last watered a specific plant — or when they've just brought a new plant home and don't know its needs.

## Access Control

Multi-user web app. Authentication: email + password (sign-up and sign-in). Flat user model — each account is fully isolated; a user sees only their own plants. No roles, no admin view, no shared plant lists. An unauthenticated user hitting a gated route is redirected to sign-in.
