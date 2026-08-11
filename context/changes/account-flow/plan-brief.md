# Account Flow — Plan Brief

> Full plan: `context/changes/account-flow/plan.md`

## What & Why

Add the S-01 account flow (sign up, sign in, sign out, and empty plant list) on top of the fully-complete F-01 auth foundation. The goal is the smallest possible UI that lets a user register, authenticate, and reach their empty plant list — proving the auth pipeline end-to-end before S-02 builds the core product loop on top of it.

## Starting Point

The project has a complete, verified auth stack (Identity, EF Core, cookie middleware, global FallbackPolicy) but no UI layer — no Pages, no Views, no wwwroot. The `LoginPath = "/account/login"` redirect is already configured in F-01 and pointing at a 404. This change makes that URL real.

## Desired End State

A user can open the app, register with email and password, and land on their empty plant list. They can sign out and sign back in. Any unauthenticated request to a protected URL redirects to the login page and returns to the original URL after sign-in. The empty plant list shows a disabled "Add plant" button as a placeholder for S-02.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| UI rendering layer | Razor Pages | First-class Identity form support, built-in anti-forgery, natural fit for form-based auth pages | Plan |
| CSS framework | Bootstrap 5 via CDN | No build step, clean forms out of the box, standard ASP.NET Core starter pattern | Plan |
| Plant list URL | `/plants` | Semantic resource URL; `/plants/add` and `/plants/{id}` follow naturally for S-02 and S-03 | Plan |
| Post-registration UX | Auto sign-in → redirect to `/plants` | Removes the "register then separately log in" friction the PRD flags as a conversion risk | Plan |
| Validation display | Inline field errors + top summary | Standard ASP.NET Core Razor Pages pattern; no custom code, works with ModelState and IdentityResult | Plan |
| Sign-out mechanism | POST form in nav bar | CSRF-safe; establishes shared nav layout pattern that all future slices inherit | Plan |
| Form validation | Server-side only | No jQuery or extra scripts; sufficient for MVP auth forms | Plan |

## Scope

**In scope:**
- Razor Pages wiring (`AddRazorPages`, `MapRazorPages`)
- Shared `_Layout.cshtml` with Bootstrap 5 CDN + auth-conditional nav bar
- `/account/login`, `/account/register`, `/account/logout` pages
- `/plants` empty-state page (message + disabled "Add plant" button)

**Out of scope:**
- Forgot password / password reset flow
- Email confirmation
- Client-side form validation
- Plant data or Plant entity (S-02)
- Any wwwroot or locally-bundled static assets

## Architecture / Approach

Razor Pages are added to the existing minimal-API project — both coexist; `MapRazorPages()` routes page requests and `MapHealthChecks()` continues to handle `/healthz`. Pages live under `Pages/` following the conventional folder-to-route mapping. All account pages carry `[AllowAnonymous]` to override the global `FallbackPolicy`. The `/plants` page is protected by that same policy with no explicit `[Authorize]` attribute. A shared `_Layout.cshtml` provides the Bootstrap nav bar and renders auth state via `User.Identity?.IsAuthenticated`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Infrastructure + layout | Razor Pages wired; shared layout with Bootstrap nav bar | `[AllowAnonymous]` omission on Login causes infinite redirect loop |
| 2. Account pages | `/account/login`, `/account/register`, `/account/logout` functional | `LocalRedirect` must be used (not `Redirect`) to prevent open redirect vulnerability |
| 3. Plant list page | `/plants` empty state; full S-01 end-to-end verified | None significant — page is a UI shell with no data layer |

**Prerequisites:** F-01 (persistence-auth-scaffold) — fully complete as of commit `5145de9`
**Estimated effort:** ~1 session across 3 short phases

## Open Risks & Assumptions

- Bootstrap CDN requires internet access during development (acceptable for after-hours solo dev context).
- The `/plants` route will conflict if S-02 uses a different URL convention — the assumption is S-02 extends this page rather than replacing it.

## Success Criteria (Summary)

- A new user can sign up, be auto-signed-in, reach `/plants` with an empty-state UI, sign out, and sign back in.
- Unauthenticated access to `/plants` redirects to login and returns to `/plants` after authentication.
- `GET /healthz` continues to return 200 without credentials (F-01 regression check).
