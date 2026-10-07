# Contributing to ControllerOS

ControllerOS is intended to become easier to extend than to centrally maintain.

You do not need to be a systems programmer to contribute.

The project is intentionally designed so maintainers do not need to personally
own every controller. Hardware owners provide physical evidence; ControllerOS
provides discovery, teaching, validation, report, fixture, and CI tooling.

## Useful contribution types

### Hardware owners

Use the public experimental Windows ZIP and headless `controlleros` CLI to
inspect and teach a selected generic HID gamepad or joystick. The complete
non-programmer workflow is in
[HARDWARE_CONTRIBUTION.md](HARDWARE_CONTRIBUTION.md). The CLI reuses the core
teaching engine, saves a reusable local definition and separate per-unit
calibration, then exports a privacy-bounded report.

Review the report before posting it with the Controller Compatibility Report
issue form. State the retail/model name only if known, connection mode, controls
tested or skipped, and evidence level. Generic HID arrays and special functions
may not be independently observable; describe them as unavailable unless the
report contains evidence the mapper understands.

The release is experimental and does not claim universal controller support.
Download the versioned ZIP from the Releases page and review its checksum
before use. The CLI is the supported interface for this preview; the WPF
desktop has not been interactively tested.

### Mapping contributors

Device definitions should be data-only whenever the hardware can be described without custom protocol code.

A mapping contribution should include:

- device identifiers needed for matching
- normalized control mapping
- a replay fixture or sanitized report that corresponds to the physical device
- connection mode
- ControllerOS version and commit
- what was physically tested
- what remains unverified

### Runtime contributors

Runtime changes should include tests.

Changes affecting ControllerScript semantics must update:

- CONTROLLERSCRIPT.md
- conformance tests
- compatibility/version notes if behavior changes

### Documentation contributors

Documentation corrections are valuable, especially when they prevent a hardware owner from needing maintainer assistance.

## Hardware evidence levels

Use explicit evidence instead of implying support:

- **maintainer-tested** — physically validated by a maintainer
- **contributor-tested** — physically validated through a community report/PR
- **mapping-only** — definition exists but current physical validation is absent
- **unverified** — inferred or experimental

Do not promote a device to a stronger evidence level without supporting evidence.

## Privacy

Hardware reports exported by ControllerOS use an allowlisted schema. Synthetic
fixtures and virtual output are software evidence only; do not represent them
as physical-device evidence.

Do not request contributors to post:

- usernames
- machine names
- account identifiers
- IP addresses
- complete USB/system inventories
- unrelated logs
- credentials/tokens

If debugging requires additional data, request the minimum specific field needed and explain why.

## Scope boundary

ControllerOS is a general controller programming platform.

Contributions should not add:

- game-specific anti-cheat bypasses
- detection-evasion logic
- hardware identity spoofing intended to bypass enforcement
- ban circumvention
- process injection
- game memory manipulation
- packet manipulation

General controller mapping, automation primitives, accessibility behavior, timing, layers, analog transforms, and device support belong in scope.

## Pull requests

Prefer small PRs with one coherent purpose.

Changes to `main` go through pull requests and the required CI check. The
current owner bypass and zero-approval policy are documented in
[GOVERNANCE.md](GOVERNANCE.md).

A PR should state:

- problem
- implementation
- tests/evidence
- compatibility impact
- hardware required to validate, if any

Avoid unrelated refactors in feature/fix PRs.

## Architecture changes

For consequential changes to:

- normalized control model
- ControllerScript semantics
- device definition schema
- profile format
- VM safety model
- output-conflict semantics

open an issue/design discussion first and document the decision.

## Development rule

The project values the smallest correct change.

Do not add generalized infrastructure for a single narrow need unless there is clear evidence it reduces total project complexity.
