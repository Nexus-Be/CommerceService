# Workspace Artifacts, Topic Notes, Confirmations & Humanized Docs

All AI Coding Agents operating in this workspace must store artifacts in the plural YuiKit folders under `.agents/` and keep temporary clutter out of the repository.

## 1. Plans -> `.agents/plans/`
- **Path & Naming:** Always save plans in `.agents/plans/YYYY-MM-DD_HHmm-plan-<slug>.md` (example: `.agents/plans/2026-11-29_0135-plan-brevo-otp-setup.md`).
- **Prohibition:** Never create or write into `.agents/plan/` (singular). Always include creation and last-updated timestamps inside the file header.

## 2. Notes -> `.agents/notes/` (One Topic = One File)
- **What to record:** Operational notes, steps taken, issues encountered and how they were resolved, and reminders/warnings for future sessions.
- **Path & Naming:** Save in `.agents/notes/YYYY-MM-DD_HHmm-<topic-slug>.md` with creation and last-updated timestamps.
- **Single-Topic Discipline (Strict):**
  - Keep all updates, troubleshooting, and findings for the same topic in **one** note file (for example, everything about email/Brevo OTP stays in one email note file).
  - As soon as work pivots to a different topic (for example, switching from email to shipping/fulfillment), **create a new note file** in `.agents/notes/` for that new topic.

## 3. User Confirmations & Settled Choices -> `.agents/decisions/`
- **What to record:** Whenever an Agent asks the user to confirm an approach, resolve an ambiguity, choose between options, or approve a rule/architecture/config change, record the question, confirmed answer, and invariants in `.agents/decisions/YYYY-MM-DD_HHmm-<decision-slug>.md`.
- **Timestamps:** Include start time and settlement time.

## 4. Human-Facing Docs -> `.agents/docs/` (Mandatory `humanizer` Skill)
- **What to record:** Whenever the user requests documentation for human readers (guides, defect reports for review, system flow overviews), save the output in `.agents/docs/<slug>.md` (unless a specific API contract path under `docs/api/` is explicitly required).
- **Mandatory `humanizer` Pass:** Every human-facing doc written in `.agents/docs/` (or `docs/`) **must** be processed with the `humanizer` skill (`humanizer/SKILL.md`) to remove AI writing tells (no `not X but Y` staging, no one-line closers, no forced triads, no em/en-dash overuse, no bold-label lists, no chatbot residue).

## 5. Workspace Hygiene
- Never leave duplicate singular folders (`.agents/plan/`, `.agents/note/`, `.agents/decision/`) or Playwright dump artifacts (`.playwright-mcp/console-*.log`, `.playwright-mcp/page-*.yml`) lying around after finishing a session.
