# COMMERCE SERVICE — AI AGENT MISSION PROTOCOL & RULES

> **Service:** CommerceService (Port 8003, Database `commerce_db:5435`)
> **Scoping:** 100% Inward-Only. Never reference outward to root or sibling services.

## Local Governance Rules
Every AI Coding Agent working inside CommerceService must strictly comply with the local rules in `.agents/rules/`:
1. [01 Clean Architecture Layers](rules/01_clean_architecture_layers.md): 4-layer structure, Feature Co-location, No-Repository over EF Core.
2. [02 API Routing & Envelopes](rules/02_api_routing_and_envelopes.md): 100% Plural route convention, pure DTO return, zero ResultFilter.
3. [03 Security Headers & Internal API](rules/03_security_headers_and_internal.md): Public controllers; internal key check temporarily disabled, buyer ownership.
4. [04 Audit & Order Logging](rules/04_audit_and_event_logging.md): AuditableEntity tracking, mutating operation audit logging.
5. [05 Error Handling & Enums](rules/05_error_handling_and_enums.md): 100% English sentence-case messages, SCREAMING_SNAKE_CASE error codes, PascalCase JSON enum serialization.
6. [06 Database & Migrations](rules/06_database_and_migrations.md): Database isolation, PostgreSQL integer enums, soft delete, deterministic seed GUIDs.

## Endpoint Workflow
Before adding or changing an endpoint, follow `.agents/skills/new-feature-endpoint/SKILL.md`.

## Verification Checklist
- `dotnet build Commerce.sln -c Release` must succeed with 0 errors.
## Workspace Artifacts (Note, Decision, Doc, Plan)
Every AI Coding Agent working in this repository must follow the local rules for the 4 workspace artifact types under `.agents/`:
- **Plans** ([`rules/plan-management.md`](rules/plan-management.md)): Save in `.agents/plans/YYYY-MM-DD_HHmm-plan-<slug>.md` using [`plans/plan-template.md`](plans/plan-template.md) (never `.agents/plan/`).
- **Topic Notes** ([`rules/note-management.md`](rules/note-management.md)): Save in `.agents/notes/YYYY-MM-DD_HHmm-<topic-slug>.md` using [`notes/note-master.template.md`](notes/note-master.template.md) (**1 topic = 1 file**; open a new file when switching topics).
- **Confirmations & Decisions** ([`rules/decision-management.md`](rules/decision-management.md)): Save every user confirmation question and settled choice in `.agents/decisions/YYYY-MM-DD_HHmm-<decision-slug>.md` using [`decisions/decision-template.md`](decisions/decision-template.md).
- **Human-Facing Docs** ([`rules/doc-management.md`](rules/doc-management.md)): Save user-requested docs for human readers in `.agents/docs/<slug>.md` and process through the `humanizer` skill first.
- **Unified Rules**: See [`rules/workspace-artifacts.md`](rules/workspace-artifacts.md).
