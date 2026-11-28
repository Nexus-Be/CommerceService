# Utility: Phased Planning & Multi-Agent Checklist Handshake Protocol

<!-- SECTION:planning-hierarchy:START -->
## 1. Hierarchical Plan Structure & Timestamp Prefix
- **File Naming & Path:** Saved strictly in `.agents/plans/YYYY-MM-DD_HHmm-plan-<plan-slug>.md` with starting and last-updated timestamps. Never create or write into `.agents/plan/` (singular).
- **User Override:** If the user specifies a custom plan name, the Agent respects the directive without forcing timestamps.
- **Three-Tier Architecture:**
  1. *Core Goals & Target Artifacts:* Concise 1-2 sentence purpose + list of affected files.
  2. *Sequential Phases:* Logical phases (Foundation ➔ Core Logic ➔ Integration ➔ Delivery).
  3. *Micro-Tasks:* Concrete, verifiable action items equipped with markdown checkboxes (`[ ]`).
<!-- SECTION:planning-hierarchy:END -->

---

<!-- SECTION:multi-agent-handshake:START -->
## 2. Multi-Agent Checklist Handshake Protocol
- **The Problem:** When multiple concurrent subagents work on a plan, allowing any single subagent to check `[x]` creates race conditions and false positives (marking work done when dependencies or siblings failed).
- **The Protocol (Handshake):**
  1. **Transient Markers Only:** When an individual subagent finishes its assigned task, it **MUST NOT mark `[x]` on the shared plan checklist**.
  2. **Recording Completion Evidence:** The subagent leaves a progress marker in the session note (`.agents/notes/`):
     `[SUBAGENT:<id>] TASK-COMPLETE: <task-id> | Evidence: <test-run-or-command-output>`
  3. **Aggregator Audit:** The final aggregator subagent (or the primary lead agent) performs a comprehensive audit:
     - Verifies that all subagent outputs pass their respective gates.
     - Confirms 100% end-to-end integration.
  4. **Official Checkbox Ticking:** Only the aggregator agent marks `[x]` on the shared checklist and purges the transient markers from the session note.
<!-- SECTION:multi-agent-handshake:END -->

---

<!-- SECTION:verification-loop-gates:START -->
## 3. Verification Loop Check Gates
Every phase in a plan ends with a mandatory Check Gate:
- **Unit Gate:** Targeted test suite passes with 100% success rate.
- **Diagnostics Gate:** 0 compilation errors and 0 linter warnings.
- **Loop Rule:** If any check fails, the agent must loop back and fix the failure within the current phase before advancing.
<!-- SECTION:verification-loop-gates:END -->
