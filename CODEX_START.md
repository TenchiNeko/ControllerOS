# Codex Start Instruction

You are implementing **ControllerOS Alpha 0.1**.

Before writing code, read these files completely:

1. AGENTS.md
2. ACCEPTANCE.md
3. ROADMAP.md
4. ARCHITECTURE.md
5. CONTROLLERSCRIPT.md
6. README.md
7. PROJECT_STATUS.md
8. CONTRIBUTING.md

Treat them as the authoritative project contract.

## Objective

Implement Alpha 0.1 end-to-end and stop when the documented completion condition is satisfied.

Do not ask for routine confirmation between milestones. Continue autonomously through all non-blocked required work.

If a step requires physical controller evidence that is not available, document the exact hardware-only validation gap in PROJECT_STATUS.md and continue every portion that can be completed using synthetic fixtures, keyboard input, Windows APIs, mocks, or automated tests.

## Required execution behavior

- Begin with M0 in ROADMAP.md.
- Keep PROJECT_STATUS.md current.
- Use the smallest correct implementation that satisfies the documented architecture and acceptance criteria.
- Keep Windows-specific code behind interfaces.
- Preserve a hardware-independent core.
- Build synthetic test coverage before depending on real controller hardware.
- Implement ControllerScript according to CONTROLLERSCRIPT.md rather than embedding unrestricted Python.
- Enforce runtime quotas and sandbox boundaries in code.
- Make timing deterministic and `wait()` non-blocking.
- Prefer data-driven controller definitions.
- Keep per-unit calibration separate from globally reusable device mappings.
- Generate only privacy-allowlisted hardware reports.
- Do not claim hardware validation that did not occur.
- Use maintained, license-compatible dependencies and document consequential dependency choices.
- Do not implement anti-cheat circumvention, detection evasion, HWID spoofing for enforcement bypass, ban evasion, process injection, game-memory manipulation, or packet manipulation.
- Do not add Alpha 0.2 features.

## Validation discipline

A milestone is COMPLETE only when its evidence exists.

Run relevant tests after each milestone and preserve concise evidence in PROJECT_STATUS.md.

Before final completion:

1. run the full build/test/lint/format/conformance suite
2. perform an independent adversarial review of architecture, runtime safety, ControllerScript sandboxing, privacy, documentation, and release readiness
3. fix all P0/P1 findings
4. rerun full validation
5. create FINAL_REPORT.md
6. make documentation match actual implemented behavior
7. ensure no required Alpha 0.1 TODO/FIXME remains
8. stop

Do not continue polishing or inventing features after the Alpha 0.1 acceptance gate passes.
