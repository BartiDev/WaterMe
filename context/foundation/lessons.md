# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Shared business logic should live in a service, not in page models

- **Context**: Pages/Plants/Edit.cshtml.cs, Pages/Plants/Index.cshtml.cs — OnPostDeleteAsync duplicated across both page models
- **Problem**: Identical delete logic (load by id+userId, Remove, SaveChanges) in two page models. A future change — soft-delete, audit logging, cascade behaviour — must be applied in both places, making it easy to miss one.
- **Rule**: [fill in — e.g. "When the same DB mutation appears in more than one page model, extract it into a PlantService method before adding a second caller."]
- **Applies to**: [fill in — e.g. "All page model handlers that share business logic with another page model"]
