# Agent Instructions

These instructions apply to Codex and other coding agents working in this repository.

## Mission

Build ControllerOS Alpha 0.1 as specified in README.md, ARCHITECTURE.md, CONTROLLERSCRIPT.md, ROADMAP.md, and ACCEPTANCE.md.

The goal is a coherent public alpha and contribution platform, not maximum feature count.

## Authority order

When instructions conflict, use this order:

1. explicit current human instruction
2. ACCEPTANCE.md
3. ROADMAP.md
4. ARCHITECTURE.md
5. CONTROLLERSCRIPT.md
6. README.md
7. implementation precedent

Do not silently change specifications to make implementation easier.

## Execution behavior

- Work through ROADMAP.md in dependency order.
- Parallelize genuinely independent tasks when supported.
- Keep shared-interface ownership explicit.
- Validate each milestone before marking it complete.
- Prefer automated evidence over claims.
- Record blockers that require physical hardware instead of fabricating success.
- Continue through required milestones without asking for routine confirmation.
- Stop when Alpha 0.1 completion conditions are satisfied.

## Scope discipline

Do not add during Alpha 0.1 unless required by acceptance criteria:

- AI configuration
- console support
- Linux/macOS support
- embedded deployment
- networking between machines
- profile marketplace
- theme/polish projects
- generalized plugin marketplace
- game-specific integrations

Do not build:

- anti-cheat circumvention
- detection-evasion logic
- HWID spoofing for enforcement bypass
- ban evasion
- process injection
- game memory inspection/manipulation
- packet manipulation

General-purpose controller automation primitives remain in scope.

## Implementation principles

- Keep controller-independent core free of Windows-specific assumptions.
- Use interfaces around input and output backends.
- Prefer synthetic tests before hardware tests.
- Prefer data-driven device definitions.
- Separate global device mapping from per-device calibration.
- Treat untrusted ControllerScript as hostile to runtime stability.
- Enforce quotas in runtime, not merely documentation.
- Never implement `wait()` by blocking the controller I/O loop.
- Keep output semantics deterministic.
- Preserve a recovery mechanism for bad profiles.
- Avoid arbitrary host capabilities in ControllerScript.

## Dependency policy

Prefer mature, maintained dependencies when they eliminate non-differentiating platform work.

Before adding a dependency:

- verify license compatibility
- record why it is needed
- avoid abandoned libraries when a maintained alternative exists
- wrap substantial platform dependencies behind project-owned interfaces
- do not copy code from unrelated projects merely because functionality is similar

## Hardware honesty

Never claim a physical controller was tested when it was not.

If a milestone can be validated with synthetic hardware, say so.

If physical evidence is required, mark the exact test as hardware-blocked and continue with all non-blocked work.

## Testing

At minimum:

- unit tests for pure components
- deterministic integration scenarios
- ControllerScript conformance tests
- malformed-input tests
- quota/runtime-failure tests
- device-definition schema tests
- privacy tests for hardware-report export

Add fuzz/property testing where it provides meaningful coverage, especially parser/schema/runtime boundaries, but do not let testing infrastructure become a separate research project.

## Completion accounting

Maintain PROJECT_STATUS.md during implementation with:

- milestone state
- evidence/commands
- blockers
- physical validation gaps
- current next action

A milestone is not complete because code exists. It is complete because its acceptance evidence exists.

## Final review

Before declaring Alpha 0.1 complete:

1. perform an independent review focused on architecture, correctness, safety boundaries, documentation, and release readiness
2. repair discovered P0/P1 defects
3. rerun the complete validation suite
4. create FINAL_REPORT.md
5. ensure docs describe what is actually implemented
6. commit all final changes
7. stop

## Stop condition

When Alpha 0.1 acceptance criteria are satisfied:

**STOP WORK.**

Do not create Alpha 0.2.
Do not invent additional features.
Do not refactor passing code solely for aesthetic preference.
Do not continue polishing indefinitely.
