# ControllerOS Roadmap

## Operating rule

This roadmap defines **Alpha 0.1**, not the entire future product.

Codex and contributors should complete required milestones, satisfy acceptance criteria, publish the release artifacts, and stop. Do not expand scope merely because another feature is interesting.

## M0 — Repository bootstrap

Required:

- project builds from documented commands
- solution/project structure exists
- formatter/linter configured
- automated tests run locally
- GitHub CI exists
- architecture/specification docs remain authoritative

Exit:
- clean checkout can execute the automated test suite

## M1 — Normalized controller core

Implement:

- ControllerState
- OutputState
- semantic control identifiers
- normalized button/axis/trigger types
- capability representation
- timestamped input events

Tests:

- type/range validation
- state transitions
- deterministic serialization where applicable

## M2 — Synthetic I/O harness

Implement:

- SyntheticInput
- RecordedOutput
- timestamped scenario runner
- fixtures for standard/noisy/off-center/missing-control devices

Exit:
- end-to-end controller behavior can be tested without Windows controller hardware

## M3 — Scheduler and runtime safety

Implement:

- event dispatcher
- continuation scheduler
- non-blocking waits
- deterministic same-time ordering
- task/timer limits
- cancellation
- runtime diagnostics

Tests:

- unrelated events continue during waits
- concurrent timers
- quota violation
- cancellation
- deterministic replay

## M4 — ControllerScript frontend

Implement the minimum language in CONTROLLERSCRIPT.md:

- lexer/parser
- AST
- semantic validation
- state
- if/else
- functions without recursion
- press/release/tap
- wait
- read/write normalized controls

Exit:
- minimum conformance program compiles

## M5 — ControllerVM

Implement:

- executable IR/bytecode
- interpreter/runtime
- instruction budget
- memory/resource accounting
- error isolation
- pending output state

Exit:
- ControllerScript conformance suite passes under synthetic input

## M6 — Profile format

Implement:

- versioned profile schema
- source + metadata representation
- validation
- safe load/save
- clear incompatibility diagnostics

Do not implement a marketplace.

## M7 — Windows device discovery

Implement:

- enumerate candidate controller/HID devices
- capture stable identifiers/capability data available through selected Windows APIs
- display discovered device information
- isolate Windows-specific code behind interfaces

Exit:
- software can distinguish known/unknown device definitions

## M8 — Device definition database

Implement:

- versioned device-definition schema
- matching rules
- loader/validator
- test fixtures
- separation of reusable mapping from per-unit calibration

Include only mappings we can legally/reliably source or generate.

## M9 — Unknown-device teaching flow

Implement a functional, minimally styled wizard for standard controls:

- requested button identification
- stick axis identification
- trigger identification
- range/center/noise sampling
- skip unsupported control
- mapping preview
- local save
- validation

Exit:
- synthetic unknown device can be taught entirely through an automated/integration test

## M10 — Sanitized hardware report

Implement:

- report generation
- privacy allowlist rather than broad system dump
- generated mapping candidate
- ControllerOS version
- validation summary
- README/instructions for attaching report to GitHub

Tests must prove known sensitive/unrelated fields are absent.

## M11 — Virtual output

Implement:

- VirtualOutput interface
- one Windows Xbox-style virtual output backend using an appropriate maintained dependency/backend
- actionable dependency/driver diagnostics
- DebugOutput fallback

Do not implement detection evasion or identity-spoofing features.

Exit:
- on a compatible Windows environment, a known synthetic action can be observed through the selected virtual-controller backend

## M12 — Minimal desktop UI

Implement only the surfaces necessary for Alpha:

- connected devices
- known/unknown status
- live normalized control inspector
- calibration/teaching entry point
- profile/source editor
- compile/runtime diagnostics
- start/stop profile
- hardware-report export
- emergency profile disable

Polish is secondary to correct behavior.

## M13 — Keyboard test input

Implement a documented keyboard-to-normalized-controller test adapter so a person can exercise the runtime without a physical controller.

## M14 — End-to-end Alpha validation

Required scenarios:

1. Synthetic SOUTH press -> ControllerScript -> delayed EAST output.
2. Wait does not block unrelated axis/button events.
3. Bad script fails compilation without affecting runtime.
4. Runtime quota violation disables offending execution safely.
5. Unknown synthetic controller -> teaching flow -> saved mapping -> recognized on next load.
6. Hardware report contains only approved fields.
7. Keyboard test input drives the same core runtime.
8. Virtual output backend succeeds when dependencies are present and fails with actionable diagnostics when absent.

## M15 — Community launch readiness

Required:

- README current
- CONTRIBUTING current
- hardware contribution instructions current
- issue templates or equivalent documented report workflow
- build/test commands verified
- Alpha limitations plainly documented
- release notes drafted
- no claims of hardware support that have not been tested or community-reported

## Alpha 0.1 completion condition

Alpha 0.1 is COMPLETE only when:

- every REQUIRED milestone above is complete or explicitly documented as blocked by unavailable physical evidence
- all unit/integration/conformance tests pass
- build succeeds from a clean checkout
- formatter/linter checks pass
- no unresolved P0/P1 defects remain
- no REQUIRED TODO/FIXME remains
- architecture docs match implementation
- a final independent review has been performed
- findings from that review are either fixed or explicitly documented
- FINAL_REPORT.md records:
  - implemented scope
  - validation commands/results
  - known limitations
  - hardware not physically validated
  - next community contribution opportunities

After these conditions are met:

**STOP.**

Do not invent Alpha 0.2 features.
Do not redesign passing components merely because alternatives exist.
Do not add AI integration, console support, embedded deployment, networking, marketplaces, or unrelated polish during Alpha 0.1.
