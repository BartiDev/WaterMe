# Account Flow Implementation Plan

## Overview

Add the S-01 account flow on top of the fully-complete F-01 foundation: registration, sign-in, sign-out, and an empty plant list that the user lands on after authentication. The change introduces Razor Pages as the UI rendering layer (the project is currently headless), establishes the shared layout that all future slices inherit, and wires the three account pages that the auth redirect pipeline expects.

## Current State Analysis

- `Program.cs` is a pure minimal-API shell with no Razor Pages or MVC registered.
- `ApplicationDbContext`, Identity services, cookie auth, and the global `FallbackPolicy = DefaultPolicy` are all active (F-01 complete and verified).
- `ConfigureApplicationCookie` already sets `LoginPath = "/account/login"` — the redirect target this plan delivers.
- No `Pages/`, `Views/`, or `wwwroot/` directory exists.
- `IdentityUser`, `UserManager<IdentityUser>`, and `SignInManager<IdentityUser>` are available in DI with no further setup.

## Desired End State

- `AddRazorPages()` and `MapRazorPages()` wired in `Program.cs`.
- Shared `_Layout.cshtml` with Bootstrap 5 (CDN) and an auth-conditional nav bar (user email + POST sign-out when authenticated; Sign in / Register links when not).
- `/account/login`, `/account/register`, and `/account/logout` pages functional.
- `/plants` page reachable by authenticated users, showing an empty-state message and a disabled "Add plant" button.
- Unauthenticated access to any non-account route → 302 to `/account/login?ReturnUrl=<path>`.
- After sign-in, the user lands on `/plants` (or the original `ReturnUrl`).
- After registration, the user is signed in and lands on `/plants`.
- After sign-out, the user is redirected to `/account/login`.

### Key Discoveries

- `Program.cs:27` — `AddIdentity` already wires `UserManager` and `SignInManager`; no additional DI setup needed for the page models.
- `Program.cs:52-53` — global `FallbackPolicy = DefaultPolicy` applies to all routes, including Razor Pages; Login, Register, and Logout pages must carry `[AllowAnonymous]`.
- `Program.cs:39-47` — `ConfigureApplicationCookie` sets `LoginPath = "/account/login"`; the Razor Pages convention for `Pages/Account/Login.cshtml` maps to `/Account/Login` which ASP.NET Core matches case-insensitively.
- No `wwwroot/` exists — Bootstrap must come from CDN, not a local static file.

## What We're NOT Doing

- No client-side (JavaScript) validation — server-side ModelState validation only for MVP.
- No "forgot password" or password-reset flow — out of S-01 scope.
- No email confirmation — `RequireConfirmedAccount = false` already set in F-01.
- No "Remember me" checkbox on the login form — cookie lifetime is controlled by `ExpireTimeSpan = 1 day` set in F-01.
- No Plant entity or data layer changes — the `/plants` page is a UI shell only; plant data belongs to S-02.
- No wwwroot, no bundled static assets — CDN only for MVP.

## Implementation Approach

Three phases in dependency order: (1) wire Razor Pages into the pipeline and create the shared layout, (2) build the account pages (login, register, logout) with `[AllowAnonymous]`, (3) build the protected plant list page that completes the S-01 flow. Each phase is independently buildable and leaves the app in a runnable state.

## Critical Implementation Details

**`[AllowAnonymous]` on Login, Register, and Logout is mandatory**: F-01's `FallbackPolicy = DefaultPolicy` requires authentication on every route by default. Without `[AllowAnonymous]` on the Login page model, unauthenticated users are redirected to `/account/login`, which is itself protected, creating an infinite redirect loop. The `[AllowAnonymous]` attribute on the class-level PageModel overrides the fallback policy for that page.

**Use `LocalRedirect`, not `Redirect`, for ReturnUrl**: Passing `returnUrl` directly to `Redirect()` opens an unvalidated redirect vulnerability. ASP.NET Core's `LocalRedirect()` throws `InvalidOperationException` for non-local URLs (any URL with a scheme or host), preventing the attack surface entirely.

**Sign-out form in the layout must use the `asp-page` form tag helper**: Writing `<form action="/account/logout" method="post">` without explicit `@Html.AntiForgeryToken()` causes a 400 Bad Request (anti-forgery validation failure). `<form asp-page="/Account/Logout" method="post">` automatically injects the anti-forgery hidden field via the tag helper.

---

## Phase 1: Razor Pages Infrastructure + Shared Layout

### Overview

Register Razor Pages in the DI container and middleware pipeline, then create the three infrastructure files (`_ViewImports.cshtml`, `_ViewStart.cshtml`, `_Layout.cshtml`) that all pages in this change share. Phase 1 is complete when the app builds and starts cleanly.

### Changes Required

#### 1. Register Razor Pages

**File**: `Program.cs`

**Intent**: Enable Razor Pages as the UI rendering layer. Two calls are needed: one on the service collection (before `builder.Build()`) and one on the app pipeline (after `UseAuthorization()`).

**Contract**: Add `builder.Services.AddRazorPages()` in the service registration block, after `AddAuthorization`. Add `app.MapRazorPages()` in the pipeline block, after `app.UseAuthorization()`.

#### 2. `_ViewImports.cshtml`

**File**: `Pages/_ViewImports.cshtml` (new file; create `Pages/` folder)

**Intent**: Apply global Razor directives so every page in `Pages/` has access to tag helpers and the `water_me` namespace without per-file imports.

**Contract**:
```cshtml
@using water_me
@using Microsoft.AspNetCore.Identity
@namespace water_me.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

#### 3. `_ViewStart.cshtml`

**File**: `Pages/_ViewStart.cshtml`

**Intent**: Set `_Layout` as the default layout for all pages so individual pages don't need to declare it.

**Contract**:
```cshtml
@{
    Layout = "_Layout";
}
```

#### 4. Shared layout

**File**: `Pages/Shared/_Layout.cshtml`

**Intent**: Establish the single shared HTML shell for the application: Bootstrap 5 from CDN (no local assets), a top navigation bar with auth-conditional content, and a `@RenderBody()` slot for page content.

**Contract**: The layout renders a Bootstrap 5 navbar using a CDN `<link>`. Nav content is conditional on `User.Identity?.IsAuthenticated`:
- Authenticated: display `User.Identity.Name` (the user's email) and a sign-out button inside `<form asp-page="/Account/Logout" method="post">` — the tag helper injects the anti-forgery token automatically.
- Not authenticated: render "Sign in" and "Register" anchor links pointing at `/account/login` and `/account/register`.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with Razor Pages registered and layout files in place

#### Manual Verification

- `dotnet run` starts without startup exceptions or Razor Pages configuration errors

**Pause here for manual confirmation before proceeding to Phase 2.**

---

## Phase 2: Account Pages

### Overview

Create the three account pages (Login, Register, Logout). All carry `[AllowAnonymous]` to override the global FallbackPolicy. Login and Register use InputModel-bound forms with server-side ModelState validation and per-field error display. Logout is a POST-only redirect with no rendered view.

### Changes Required

#### 1. Login page

**Files**: `Pages/Account/Login.cshtml` + `Pages/Account/Login.cshtml.cs`

**Intent**: Deliver the sign-in form at `/account/login` — the target of all auth redirects configured in F-01. On success, redirect to `/plants` or the `ReturnUrl` query parameter (whichever was set by the auth middleware). On failure, redisplay the form with an error message.

**Contract** (`Login.cshtml.cs`):
- Class `LoginModel : PageModel`, decorated `[AllowAnonymous]`.
- Nested `InputModel` with: `Email` (`[Required, EmailAddress]`), `Password` (`[Required, DataType(DataType.Password)]`).
- `[BindProperty] public InputModel Input { get; set; }` + `public string? ReturnUrl { get; set; }`.
- `OnGetAsync(string? returnUrl)`: assign `ReturnUrl = returnUrl ?? Url.Content("~/plants")`.
- `OnPostAsync(string? returnUrl)`: if `!ModelState.IsValid`, return `Page()`. Call `_signInManager.PasswordSignInAsync(Input.Email, Input.Password, isPersistent: false, lockoutOnFailure: false)`. On success, return `LocalRedirect(returnUrl ?? Url.Content("~/plants"))`. On failure, `ModelState.AddModelError(string.Empty, "Invalid email or password.")` and return `Page()`.

**Contract** (`Login.cshtml`): Form with `asp-page` pointing to self, `method="post"`, `asp-antiforgery="true"`. Two inputs bound with `asp-for`. Validation summary with `asp-validation-summary="All"` and per-field spans with `asp-validation-for`. A hidden `ReturnUrl` field passes the value through the POST.

#### 2. Register page

**Files**: `Pages/Account/Register.cshtml` + `Pages/Account/Register.cshtml.cs`

**Intent**: Deliver the sign-up form at `/account/register`. On success, auto-sign-in the new user and redirect to `/plants`. On failure (validation or identity errors), redisplay the form with errors.

**Contract** (`Register.cshtml.cs`):
- Class `RegisterModel : PageModel`, decorated `[AllowAnonymous]`.
- Nested `InputModel` with: `Email` (`[Required, EmailAddress]`), `Password` (`[Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password)]`), `ConfirmPassword` (`[DataType(DataType.Password), Compare("Password", ErrorMessage = "Passwords do not match.")]`).
- `OnPostAsync()`: if `!ModelState.IsValid`, return `Page()`. Call `_userManager.CreateAsync(new IdentityUser { UserName = Input.Email, Email = Input.Email }, Input.Password)`. If the result succeeds, call `_signInManager.SignInAsync(user, isPersistent: false)` then `return RedirectToPage("/Plants/Index")`. Otherwise, loop `result.Errors` and call `ModelState.AddModelError(string.Empty, error.Description)` for each, then return `Page()`.

**Contract** (`Register.cshtml`): Form with three inputs (Email, Password, ConfirmPassword), validation summary at top, per-field validation spans. A link to `/account/login` ("Already have an account?").

#### 3. Logout page

**Files**: `Pages/Account/Logout.cshtml` (minimal, no view content) + `Pages/Account/Logout.cshtml.cs`

**Intent**: Handle the POST sign-out request submitted from the layout nav bar. Sign the user out and redirect to the login page. The page never renders its view — every code path is a redirect.

**Contract** (`Logout.cshtml.cs`):
- Class `LogoutModel : PageModel`, decorated `[AllowAnonymous]`.
- `OnPostAsync()`: call `await _signInManager.SignOutAsync()`, return `RedirectToPage("/Account/Login")`.
- `OnGet()`: return `RedirectToPage("/Account/Login")` (graceful handling if navigated to directly).

**Contract** (`Logout.cshtml`): Minimal Razor file — `@page` and `@model` directives only. The view body is never rendered.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with all account pages in place

#### Manual Verification

- Navigate to `/account/register` — registration form renders with Bootstrap styling
- Register with a valid email and password meeting the requirements → auto-signed in and redirected to `/plants` (404 at this point — expected until Phase 3)
- Navigate to `/account/login` — login form renders
- Sign in with valid credentials → redirected to `/plants`
- The nav bar displays the signed-in user's email and a "Sign out" button
- Click "Sign out" → signed out and redirected to `/account/login`
- Register with short password or mismatched confirm password → inline and summary errors appear
- Login with wrong password → "Invalid email or password." error appears
- Navigate directly to `/plants` while unauthenticated → redirected to `/account/login?ReturnUrl=%2Fplants`

**Pause here for manual confirmation before proceeding to Phase 3.**

---

## Phase 3: Plant List Page

### Overview

Create the `/plants` Razor Page as the authenticated home screen. The page shows an empty-state message and a disabled "Add plant" button. It is protected by the global FallbackPolicy (no `[Authorize]` attribute needed). Phase 3 is complete when the full S-01 end-to-end flow works.

### Changes Required

#### 1. Plant list index page

**Files**: `Pages/Plants/Index.cshtml` + `Pages/Plants/Index.cshtml.cs`

**Intent**: Deliver the authenticated home screen at `/plants`. The page confirms the auth flow is complete — a signed-in user landing here sees the empty-state UI; an unauthenticated user is redirected to login by the global policy. The "Add plant" button is present but disabled, establishing the placeholder for S-02.

**Contract** (`Index.cshtml.cs`):
- Class `IndexModel : PageModel`. No `[Authorize]` attribute — the global `FallbackPolicy` already enforces authentication.
- `OnGet()`: no-op (no data to load in S-01).

**Contract** (`Index.cshtml`): Page heading ("My Plants"). Empty-state block: a centered message ("You have no plants yet") and a `<button class="btn btn-primary" disabled>Add plant</button>`. The disabled attribute will be removed and the button wired up in S-02.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds

#### Manual Verification

- Full S-01 end-to-end flow:
  - Sign up → auto-signed in → land on `/plants` with empty-state message and disabled "Add plant" button
  - Sign out from `/plants` nav → redirected to `/account/login`
  - Sign in with existing credentials → redirected to `/plants`
  - Open a private/incognito window and navigate to `/plants` → redirected to `/account/login?ReturnUrl=%2Fplants`; after login → redirected back to `/plants`
- No regressions: `GET /healthz` still returns 200 without credentials

**Pause here for manual confirmation before committing.**

---

## Testing Strategy

### Manual Testing Steps

1. Register a new account — verify auto-sign-in and redirect to `/plants`.
2. Sign out — verify redirect to `/account/login`.
3. Sign in with the same credentials — verify redirect to `/plants`.
4. Attempt registration with a 5-character password — verify "Passwords must be at least 6 characters" error.
5. Attempt registration with mismatched confirm password — verify "Passwords do not match." error.
6. Attempt login with wrong password — verify "Invalid email or password." error.
7. Navigate to `/plants` in a private window (unauthenticated) — verify redirect to `/account/login?ReturnUrl=%2Fplants`; log in → verify redirect back to `/plants`.
8. Verify `GET /healthz` returns 200 in all states (no regression from F-01).

### Unit Tests

No test project exists yet. When added (per `AGENTS.md`), account flow tests should cover:
- `RegisterModel.OnPostAsync` — success path (user created + signed in), duplicate email path, short password path.
- `LoginModel.OnPostAsync` — success path, wrong password path, ModelState-invalid path.

## References

- Roadmap S-01: `context/foundation/roadmap.md:64–74`
- PRD FR-001, FR-002, Access Control: `context/foundation/prd.md:61–64, 106–109`
- F-01 plan (prerequisite, fully complete): `context/changes/persistence-auth-scaffold/plan.md`
- Cookie auth and FallbackPolicy wiring: `Program.cs:39–53`
- Identity services registration: `Program.cs:27–37`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Razor Pages Infrastructure + Shared Layout

#### Automated

- [x] 1.1 `dotnet build` succeeds with Razor Pages registered and layout files in place — 9cf9cad

#### Manual

- [x] 1.2 `dotnet run` starts without startup exceptions or Razor Pages configuration errors — 9cf9cad

### Phase 2: Account Pages

#### Automated

- [x] 2.1 `dotnet build` succeeds with all account pages in place

#### Manual

- [x] 2.2 Registration form renders at `/account/register`
- [x] 2.3 Register with valid credentials → auto-signed in, redirected to `/plants` (404 expected)
- [x] 2.4 Login form renders at `/account/login`
- [x] 2.5 Sign in with valid credentials → redirected to `/plants`
- [x] 2.6 Nav bar shows user email and "Sign out" button
- [x] 2.7 Sign out → redirected to `/account/login`
- [x] 2.8 Validation errors display correctly for short password and mismatched confirm
- [x] 2.9 Wrong password → "Invalid email or password." error shown
- [x] 2.10 Unauthenticated `/plants` → redirected to `/account/login?ReturnUrl=%2Fplants`

### Phase 3: Plant List Page

#### Automated

- [x] 3.1 `dotnet build` succeeds

#### Manual

- [ ] 3.2 Full S-01 end-to-end flow: sign up → `/plants` (empty state + disabled button) → sign out → sign in → `/plants`
- [ ] 3.3 Unauthenticated `/plants` → redirected to login → after sign-in → back to `/plants`
- [ ] 3.4 `GET /healthz` returns 200 (no regression)
