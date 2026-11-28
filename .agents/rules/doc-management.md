# Utility: Human-Centric Documentation & Manual Testing Protocol

<!-- SECTION:doc-audience:START -->
## 1. Bifurcated Audience Architecture: `.agents/docs/`, Share Docs & Repo Docs
Human-centric documentation is strictly decoupled from `.agents/notes/` and must be 100% free of internal AI deliberations, scratchpads, prompt reasoning, or raw error logs.

- **Default Location for User-Requested Human Docs:** Whenever the user asks to write documentation for human readers, save it under `.agents/docs/<slug>.md` (or `docs/share/` / `docs/repo/` when specifically scoped for repo/external handoff) **and MUST process it through the `humanizer` skill** before finalizing.

### Branch A: External / Standalone Share Docs (`docs/share/<slug>.md` or `.agents/docs/<slug>.md`)
- **Target Audience:** External stakeholders, clients, product managers, or users who **DO NOT have access to the repository**.
- **Location & Naming:** Stored under `docs/share/` with a clean, semantic kebab-case filename **WITHOUT timestamp prefixes** (e.g., `docs/share/api-overview.md`, `docs/share/user-manual.md`).
- **Strict Invariant (Zero Repo References):**
  - **ABSOLUTELY FORBIDDEN** to use internal repo paths (e.g., `src/...`, `.agents/...`), relative code links, or wikilinks (`[[...]]`). External readers will see broken links and confusion.
  - All concepts, architectures, and behaviors must be explained in plain, intuitive prose with self-contained Mermaid diagrams that render cleanly on their own.

### Branch B: Internal / Codebase-Aware Reader Docs (`docs/repo/<slug>.md`)
- **Target Audience:** Internal team members, developers, QA engineers, and code reviewers who **HAVE cloned the repository**.
- **Location & Naming:** Stored under `docs/repo/` with a clean, semantic kebab-case filename (e.g., `docs/repo/architecture-deep-dive.md`).
- **Codebase Navigation:** Direct markdown links to source files (e.g., `[AuthService](src/services/auth.service.ts)`), module boundaries, build commands, and repo test scripts are permitted and encouraged.
<!-- SECTION:doc-audience:END -->

---

<!-- SECTION:feature-and-testing-guides:START -->
## 2. Feature Walkthroughs & Manual Testing Guides
Whenever a feature or user story completes:
1. **User Guide Section:** Explains what the feature does, why it exists, and how an operator interacts with it.
2. **Manual Test Walkthrough:** Step-by-step instructions for human verification:
   - *Preconditions:* Setup requirements or seed data.
   - *Action Steps:* Exact buttons to click or public API endpoints to invoke.
   - *Expected Visual Result:* Observable outcome on screen or in responses.
   - *Branch Adaptation:* For `docs/share/`, tests focus purely on user-visible inputs/outputs without repo commands. For `docs/repo/`, tests may reference test commands or fixtures in the codebase.
<!-- SECTION:feature-and-testing-guides:END -->

---

<!-- SECTION:writing-style-rules:START -->
## 3. Communication, User Override & Clean Delivery
- **Lead with the Answer:** State what is true or what to do affirmatively first, then provide supporting context.
- **Self-Contained:** A reader must understand the document without reading past conversation histories.
- **Visual Over Textual:** Use Mermaid flowcharts, sequence diagrams, and markdown tables to convey complex architectures.
- **User Override & Clean Delivery Mode:**
  - If the user provides a custom filename or requests clean documents for human reading:
    1. The Agent strictly adopts the requested filename without prepending timestamps.
    2. The Agent strips all internal AI clutter: no prompt reasoning, no scratchpad dumps, no internal tags, and no conversational logging debris.
<!-- SECTION:writing-style-rules:END -->

---

<!-- SECTION:human-writing-discipline:START -->
## 4. Anti-AI-ism and Human Writing Discipline
When authoring documentation for humans (`docs/share/`, `docs/repo/`, README, or Wiki), the agent must eliminate automated chatbot patterns and write in direct, uninflated prose:

1. No Staging or Theatrics
- Do not use the "Not X but Y" contrast formula (e.g. "It's not just a tool, it's a statement"). State the point directly.
- Cut dramatic one-line closers and isolated fragments ("That is the real win.", "Let that sink in.", "No aesthetic prior. No nostalgia.").
- Cut sayings that sound deep ("at its core", "the currency of trust", "a trap for teams").
- Cut staged run-ups and fake candor ("Let's dive in", "Here is what you need to know", "Honestly?", "Real talk").
- Cut arguments with unmade objections ("This isn't about", "To be clear", "Don't get me wrong").

2. Restrained Mechanics and Zero Formatting by Rule
- Do not use em dashes (—) or en dashes (–) as universal connectors. Use periods, commas, colons, or clean clauses instead.
- Do not force triads of adjectives, examples, or lessons when one or two suffice.
- Do not bold the first label of every list item. Use bold only when an isolated term needs visual emphasis.
- Do not use decorative title case, emojis, or horizontal rules between short sections.

3. Factual Grounding over Inflated Prose
- Ban overused AI vocabulary: delve, tapestry, crucial, pivotal, enduring, testament, bolster, showcase, interplay, intricate, vibrant.
- Ban sales language: stands as a testament, rich, profound, nestled, in the heart of, exciting times ahead.
- Use plain verbs (is, are, has) instead of evasive substitutes (serves as, operates as, boasts, features).
- Cut shallow -ing tails: highlighting, underscoring, ensuring, reflecting, symbolizing.

4. Clean Delivery
- Remove chatbot residue: "I hope this helps", "Certainly!", "Great question!".
- Remove cutoff and knowledge disclaimers: "as of my training", "details are limited".
- Write about the subject, not about the document or drafting method ("this document was compiled from...").

5. Reader-First Architecture
- Lead with the answer or action. State what is true affirmatively before giving reasons.
- Never force the reader to read a diagnosis or history before reaching the decision.
<!-- SECTION:human-writing-discipline:END -->
