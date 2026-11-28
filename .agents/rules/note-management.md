# Utility: Agent Workspace & Pending Doubts Note Protocol

<!-- SECTION:workspace-purpose:START -->
## 1. Workspace Identity & Audience
- **Target Audience:** Exclusively for AI Agents and Subagents.
- **Tone & Format:** Fact-dense, bullet points, structured logs, and token-optimized summaries.
- **File Naming & Location:** Stored in `.agents/notes/YYYY-MM-DD_HHmm-<topic-slug>.md` with an exact creation timestamp header (e.g. `2026-11-23_0130-auth-pipeline.md`).
  - *Structure:* Date (`YYYY-MM-DD`), separator (`_`), compact 24h time (`HHmm`), separator (`-`), and topic slug (`<topic-slug>`).
- **Working Notes vs Informational Files:**
  - *Working Notes & Scratchpads:* MUST carry the compact Date + Time prefix to indicate temporal sequence and distinguish work-in-progress logs.
  - *Informational & Permanent Specs:* Maintained without timestamp prefixes (e.g., `spec.md`, `contracts.ts`, `GLOSSARY.md`).
- **What to Record:** Operational actions taken, issues encountered and how they were resolved, and reminders/notes worth keeping for future sessions.
- **Single-Topic Discipline:** All discussion and work notes within one topic remain in a single continuous file (e.g., all email/Brevo notes stay in one email note file). As soon as the work pivots to a new topic (e.g., switching from email to shipping/fulfillment), **create a new note file** in `.agents/notes/YYYY-MM-DD_HHmm-<topic-slug>.md`.
- **Invocation:** Record notes whenever capturing operational progress, troubleshooting findings, or reminders for later reading, as well as when explicitly commanded by the user.
- **User Override:** If the user specifies a custom file name or requests omitting timestamps, the Agent strictly obeys and strips internal AI clutter.
<!-- SECTION:workspace-purpose:END -->

---

<!-- SECTION:turn-logging:START -->
## 2. Structured Session Turn Logging
When a structured note session is initiated or updated upon user instruction, each recorded turn captures four mandatory components:
1. ⏱️ **Exact Timestamp:** Day, Date, Time (HH:MM UTC+7).
2. 📌 **User Intent Summary:** Concise summary of user instructions.
3. 💬 **Focused Agent Response:** Core solution, decisions, and architectural points.
4. 💡 **Agent Internal Scratchpad:** Working hypotheses, temporary state, and context pointers for other agents.
<!-- SECTION:turn-logging:END -->

---

<!-- SECTION:pending-doubts:START -->
## 3. Pending Doubts Memo (Deferred Clarification)
- **Purpose:** During discussion, analysis, or planning, if the agent encounters technical questions or ambiguous requirements that cannot or should not be asked immediately (to avoid interrupting the user's flow), the agent **must log them into the Pending Doubts section**.
- **Format:**
  - `[ ] DOUBT-<id>:` Unresolved ambiguity + impact + proposed recommendation.
- **Resolution:** The agent reviews this section at appropriate transition points (such as before planning or before finalizing decisions) and presents pending doubts to the user for resolution.
<!-- SECTION:pending-doubts:END -->

---

<!-- SECTION:subagent-communication:START -->
## 4. Subagent Memo Pad & Inter-Agent Coordination
- **Purpose:** Channel for concurrent or delegated subagents to exchange context and hand off state without cluttering user-facing conversations.
- **Protocol:**
  - When a subagent starts or finishes a sub-task, it records a signed message:
    `[SUBAGENT:<id>] <timestamp> Status: <in-progress|completed|blocked> | Memo: <details> | Artifacts: <paths>`
  - Subagents read previous entries to maintain situational awareness before modifying shared codebase seams.
<!-- SECTION:subagent-communication:END -->
