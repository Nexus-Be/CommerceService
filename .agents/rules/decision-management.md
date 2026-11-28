# Utility: Architectural Decision Record & Zero-Doubt Closure Protocol

<!-- SECTION:decision-purpose:START -->
## 1. Decision Identity & Mandatory Invariants
- **Purpose:** Serves as an immutable reference point documenting user confirmations, resolved agent questions, technical choices, architectural boundaries, and agreed scope. Whenever the agent asks the human operator to confirm an approach or decision, record the question and confirmed answer here.
- **File Naming & Path:** Saved in `.agents/decisions/YYYY-MM-DD_HHmm-<decision-slug>.md` with starting and settled timestamps.
- **Core Principles:** Concise, factual, unambiguous. Designed for rapid scanning without reading thousands of chat lines.
- **User Override:** If the user specifies an alternative naming format, the Agent strictly respects the directive.
<!-- SECTION:decision-purpose:END -->

---

<!-- SECTION:zero-doubt-closure:START -->
## 2. Zero-Doubt Closure Protocol (Mandatory Pre-Closure Audit)
- **Trigger:** When a topic reaches consensus, work completes, or the user pivots to an unrelated problem scope.
- **Audit Procedure:**
  1. Before finalizing the decision and closing the topic, the agent **MUST scan the active session log in `.agents/notes/`**, specifically the `Pending Doubts Memo` section.
  2. If any open doubts or unresolved questions remain:
     - The agent **CANNOT silently close the issue**.
     - The agent **MUST notify the human operator**:
       `"⚠️ Pending Doubts Detected: The following questions remain unresolved before closing this topic: [list]. How would you like to settle them?"`
  3. A topic reaches official closure ONLY when all doubts are resolved or explicitly deferred by the operator.
<!-- SECTION:zero-doubt-closure:END -->

---

<!-- SECTION:mandatory-sections:START -->
## 3. Mandatory Document Structure
Every decision document must include four mandatory sections:
1. ⏱️ **Context Timestamps:** Conversation start time & decision settlement time.
2. 🎯 **The Core Decision:** Explicit statement of the chosen technical approach and underlying rationale.
3. 🔒 **Mandatory Invariants:** Strict operational constraints that cannot be violated downstream.
4. 🔴 **Explicit Non-Goals:** Clear boundaries of what is deliberately excluded from scope.
<!-- SECTION:mandatory-sections:END -->

---

<!-- SECTION:vulnerability-decision-protocol:START -->
## 4. Vulnerability & Verification Decision Protocol
- **Trigger:** Mandatory whenever experimental testing commands (TDD Red-Green, `/interrogate`, adversarial probes, model scanning, security reviews) discover a defect, flaw, vulnerability, or architectural bottleneck.
- **Immediate Logging Requirement:** The moment a remediation approach is agreed upon between the user and the agent, the Agent **MUST immediately record a formal decision record in `.agents/decisions/`** before writing code.
- **Three Mandatory Components:**
  1. 🔴 **Flaw & Proof (Lỗ hổng & Bằng chứng):** Defect name, mechanism, and real verification command with verbatim output proving the failure.
  2. 🟢 **Remediation Solution (Phương án khắc phục):** Specific architectural or code fix chosen to eliminate the root cause.
  3. 👤 **Attribution & Authority (Nguồn gốc đề xuất & Thẩm quyền):**
     - `Proposed by: [Human Operator (Người Dùng) | AI Agent recommendation (Agent đề cử)]`
     - `Decided by: Human Operator (Người Dùng)`
<!-- SECTION:vulnerability-decision-protocol:END -->
