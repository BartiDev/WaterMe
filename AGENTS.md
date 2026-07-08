# Repository Guidelines

## Commit & Pull Request Guidelines
Recent commits use short, task-numbered subjects such as `#1.3 bootstrap project`. Keep that format: `#<task> <imperative summary>`.

Pull requests should include:

- A short description of the change and why it was made.
- Links to related notes or issue references.
- Screenshots or request/response samples when UI or API behavior changes.
- Confirmation that docs in `context/foundation/` were updated when architecture or scope changed.

## Project Structure & Module Organization
Application source lives in the repo root (`water-me.csproj`, `Program.cs`, `appsettings*.json`). Project context lives under `context/`.

- `context/foundation/` stores long-lived documents such as product requirements and stack decisions.
- `context/changes/` is reserved for change-specific plans, research, and reviews.
- `context/archive/` holds superseded documents that should no longer be edited in place.
- Root files such as `README.md`, `CLAUDE.md`, and `idea-notes.md` provide quick project context.

## Testing Guidelines
No test project exists yet. When adding one, place it under `tests/` and mirror the production namespace structure. Name test files after the target class, such as `PlantServiceTests.cs`. Include unit tests for business rules and integration tests for authentication or external AI calls.

## Build, Test, and Development Commands

@README.md

## Coding Style & Naming Conventions

- Keep filenames aligned with primary type names, for example `PlantController.cs`.
- Keep controllers thin — handler methods should delegate to a service class. Store secrets and connection strings in appsettings.json / environment variables, never in *.cs source files.
