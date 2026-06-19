---
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
---

## Why this stack

Water Me is a solo-built, after-hours greenfield web-app with a 3-week MVP timeline and two technology-forcing features: email+password authentication and an AI-assisted plant watering-schedule suggestion step. ASP.NET Core (`dotnet`) is the recommended default for `(web-app, dotnet)` and clears all four agent-friendly gates: strongly typed C# with built-in DI and OpenAPI, convention-based project layout, well-represented in training data, and versioned official Microsoft documentation. Auth is first-class via ASP.NET Core Identity, covering FR-001 and FR-002 without extra setup. The AI call to retrieve watering schedules (Anthropic or OpenAI .NET SDK added as a NuGet package) is standard add-on territory and poses no scaffolding friction. Deployment targets Azure App Service — the Microsoft-native default for .NET and the lowest-friction path for a solo developer. CI runs on GitHub Actions with auto-deploy-on-merge, matching the starter's standard shape. Bootstrapper confidence is verified, so scaffolding will be smooth.
