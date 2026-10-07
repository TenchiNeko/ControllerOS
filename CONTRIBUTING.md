# Contributing to ControllerOS

ControllerOS is intended to become easier to extend than to centrally maintain.

You do not need to be a systems programmer to contribute.

## Useful contribution types

### Hardware owners

The Alpha 0.1 teaching screen only accepts samples from its built-in synthetic
unknown-device fixture. It cannot yet collect raw reports from a physical HID
device. A report exported by that screen describes the synthetic fixture; do
not submit it as evidence for a physical controller.

For a physical controller contribution, open the hardware-support issue and
include the retail/model name, connection mode, Windows version, the controls
you tested, and how you observed their raw values. Mark untested controls and
the evidence level explicitly. Do not include device paths, serial numbers,
usernames, machine names, IP addresses, unrelated USB inventory, or raw system
logs. A data-only definition can be proposed with a small synthetic fixture
once the raw control identifiers and ranges are known.

### Mapping contributors

Device definitions should be data-only whenever the hardware can be described without custom protocol code.

A mapping contribution should include:

- device identifiers needed for matching
- normalized control mapping
- a synthetic fixture or a sanitized report that corresponds to the physical device
- connection mode
- ControllerOS version
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

Hardware reports exported by ControllerOS use an allowlisted schema, but the
Alpha 0.1 desktop currently exports only synthetic teaching data. Do not
represent that report as physical-device evidence.

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
