---
bootstrapped_at: 2026-07-08T14:36:00Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: water-me
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "dotnet list package --vulnerable --include-transitive"
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: water-me
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: true
  has_background_jobs: false
```

**Why this stack:** Water Me is a solo-built, after-hours greenfield web-app with a 3-week MVP timeline and two technology-forcing features: email+password authentication and an AI-assisted plant watering-schedule suggestion step. ASP.NET Core (`dotnet`) is the recommended default for `(web-app, dotnet)` and clears all four agent-friendly gates: strongly typed C# with built-in DI and OpenAPI, convention-based project layout, well-represented in training data, and versioned official Microsoft documentation. Auth is first-class via ASP.NET Core Identity, covering FR-001 and FR-002 without extra setup. The AI call to retrieve watering schedules (Anthropic or OpenAI .NET SDK added as a NuGet package) is standard add-on territory and poses no scaffolding friction. Deployment targets Azure App Service — the Microsoft-native default for .NET and the lowest-friction path for a solo developer. CI runs on GitHub Actions with auto-deploy-on-merge, matching the starter's standard shape. Bootstrapper confidence is verified, so scaffolding will be smooth.

## Pre-scaffold verification

| Signal      | Value                                                   | Severity | Notes                                                            |
| ----------- | ------------------------------------------------------- | -------- | ---------------------------------------------------------------- |
| npm package | not run                                                 | n/a      | dotnet starter does not use an npm create-* CLI                  |
| GitHub repo | not run                                                 | n/a      | docs_url (learn.microsoft.com/aspnet/core) is not a GitHub URL   |

No recency signals available for the dotnet starter. The framework is maintained by Microsoft and ships as part of the .NET SDK; freshness is governed by the SDK version installed locally (`dotnet --version`), not by npm or a GitHub repo.

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n water-me -o .bootstrap-scaffold --no-restore` *(prior session — exact invocation not captured; inferred from project name in scaffold)*
**Strategy**: scaffold into a temp directory then move files up (subdir-then-move)
**Exit code**: 0 (prior run succeeded)
**Files moved**: 7 (water-me.csproj, Program.cs, appsettings.json, appsettings.Development.json, water-me.http, NuGet.Config, Properties/launchSettings.json)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: no .gitignore present in scaffold; cwd has a file named `.gitgnore` (note: filename typo — missing 'i') which git does not read as an ignore file. See Next steps.
**.bootstrap-scaffold cleanup**: deleted

**Note on prior run**: `.bootstrap-scaffold/` was found already populated with a complete scaffold (including `bin/` and `obj/` build artifacts) when this run started. The scaffold CLI step was skipped — files were moved up from the existing temp directory. `bin/` and `obj/` were not moved (generated artifacts; regenerated cleanly by `dotnet restore` which was run post-move-up).

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: not distinguished by this tool — `--include-transitive` flag covers both in a single pass

Clean tree. The webapi template ships only `Microsoft.AspNetCore.OpenApi 9.0.11` as a direct dependency; no advisories reported against any package in the resolved graph.

## Hints recorded but not acted on

| Hint                    | Value                  |
| ----------------------- | ---------------------- |
| bootstrapper_confidence | verified               |
| quality_override        | false                  |
| path_taken              | standard               |
| self_check_answers      | null                   |
| team_size               | solo                   |
| deployment_target       | azure-app-service      |
| ci_provider             | github-actions         |
| ci_default_flow         | auto-deploy-on-merge   |
| has_auth                | true                   |
| has_payments            | false                  |
| has_realtime            | false                  |
| has_ai                  | true                   |
| has_background_jobs     | false                  |

These fields were carried through for audit-trail completeness. A future skill will act on `has_auth`, `has_ai`, `deployment_target`, `ci_provider`, and `ci_default_flow` to wire up Identity, AI SDK, Azure App Service config, and GitHub Actions CI.

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:

- **Fix the gitignore typo**: rename `.gitgnore` → `.gitignore` (`git mv .gitgnore .gitignore`) and update the ignore patterns from `.bootstrap-scaffold/bin/` and `.bootstrap-scaffold/obj/` to just `bin/` and `obj/` so the .NET build output is properly excluded.
- `dotnet build` — compile the scaffolded project (restore already ran; binary output lands in `bin/`).
- `dotnet run` — start the local dev server; the default template exposes `GET /weatherforecast` and the OpenAPI spec at `/openapi/v1.json`.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep. *(None were created in this run.)*
- Address audit findings per your project's risk tolerance — the full breakdown is in this log. *(0 findings — no action needed.)*
- Add ASP.NET Core Identity (`Microsoft.AspNetCore.Identity.EntityFrameworkCore`) and a database provider when you're ready to implement auth (FR-001 / FR-002 from the PRD).
- Add the Anthropic or OpenAI .NET SDK NuGet package when you're ready to wire up the AI watering-schedule feature.
