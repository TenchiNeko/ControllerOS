# ControllerOS Project Status

Last updated: 2026-10-07

## Release history

**Alpha 0.1 — implementation complete; public release not published.**

The Alpha 0.1 acceptance and final report are historical records. This status
file tracks the current Headless Community Bridge phase separately.

## Alpha 0.1 milestones (historical snapshot)

| Milestone | State | Evidence / limitation |
| --- | --- | --- |
| M0 Repository bootstrap | COMPLETE | .NET 10.0.401 solution, CI workflow, editor configuration; clean-checkout validation recorded in FINAL_REPORT.md |
| M1 Normalized controller core | COMPLETE | Typed standard controls, bounded capabilities, immutable input/output snapshots, timestamped transitions, deterministic JSON; Core tests pass |
| M2 Synthetic I/O harness | COMPLETE | Bounded SyntheticInput, RecordedOutput, scenario capture, noisy/off-center/missing-control and unknown-device fixtures; end-to-end script replay tests pass |
| M3 Scheduler and runtime safety | COMPLETE | Deterministic input/timer ordering, idle `AdvanceTo`, non-blocking waits, quotas, cancellation, output reset/failure isolation; safety tests pass |
| M4 ControllerScript frontend | COMPLETE | Lexer, parser, source diagnostics, typed validation, state/conditions/functions and v0.1 controls; conformance tests pass |
| M5 ControllerVM | COMPLETE | Bytecode interpreter, instruction/call/task/timer quotas, handler isolation and coherent output commits; synthetic scenario tests pass |
| M6 Profile format | COMPLETE | Versioned JSON with duplicate/unknown property rejection, script validation, bounded reads and atomic save; profile tests pass |
| M7 Windows device discovery | COMPLETE | SetupAPI/HidP enumeration and native XInput polling; Windows VM discovered the virtual Xbox HID device and read normalized events |
| M8 Device definition database | COMPLETE | Versioned strict schema, stable matching, separate mapping/calibration files, bounded validation; schema and save/load tests pass |
| M9 Unknown-device teaching flow | COMPLETE | Core teaching and desktop flow identify synthetic button/axis/trigger samples, capture calibration, skip controls, preview/save and reload; no physical raw-HID teaching path is implemented |
| M10 Sanitized hardware report | COMPLETE | Report schema v2 uses an allowlist and contiguous anonymous ordinal IDs; serializer validation rejects caller-supplied labels, with privacy tests for host/user-like labels, paths, and address-like IDs |
| M11 Virtual output | COMPLETE | HIDMaestro v1.11.0 Xbox-style output; VM read back synthetic EAST, trigger and axis output through XInput, saw disconnect neutralization, and passed the missing-DLL diagnostic |
| M12 Minimal desktop UI | COMPLETE | WPF Release build includes device list, keyboard/XInput selection, live inspector, profile editor, diagnostics, synthetic teaching/report, start/stop and emergency disable; VM had no interactive user session for a visual launch check |
| M13 Keyboard test input | COMPLETE | Documented key map, repeat suppression, focus/deactivation release and keyboard-to-runtime tests pass |
| M14 End-to-end Alpha validation | COMPLETE | Core suite covers synthetic delayed output, unrelated input during waits, failed compilation while a runtime is active, quotas, teaching/save/recognition, report privacy and keyboard runtime; Windows VM adapter smoke passes |
| M15 Community launch readiness | COMPLETE | README, Architecture, language/contribution docs, issue template, release notes, CI, documented commands and evidence levels updated |

## Alpha 0.1 validation record

Run from the repository root with the .NET 10 SDK:

```sh
dotnet restore ControllerOS.sln
dotnet build ControllerOS.sln --configuration Release --no-restore
dotnet test ControllerOS.sln --configuration Release --no-build
dotnet format ControllerOS.sln --verify-no-changes --no-restore
```

Alpha 0.1 passed 36/36 tests. Its original Release build, format verification,
Windows adapter check, and limitations are recorded in FINAL_REPORT.md.

## Headless Community Bridge

The Headless Community Bridge adds a Windows CLI over existing services,
selected-device raw HID capture, physical teaching through the existing
calibration engine, privacy-bounded report v3, local per-unit calibration, and
CI packaging for an experimental self-contained Windows CLI.

Validation commands from a clean checkout:

```sh
dotnet restore ControllerOS.sln
dotnet build ControllerOS.sln --configuration Release --no-restore
dotnet test ControllerOS.sln --configuration Release --no-build
dotnet format ControllerOS.sln --verify-no-changes --no-restore
```

The current workspace Release build passes with zero warnings/errors; the suite
passes 44/44 tests; format verification passes; and `controlleros self-test
--json` passes 5/5 checks on Linux. The self-test marks Windows-only output and
HID enumeration as not applicable on Linux. A self-contained `win-x64` publish
also succeeds locally and includes the pinned output dependency and notices.

The phase acceptance ledger is HEADLESS_BRIDGE_ACCEPTANCE.md. Its final review
evidence, CI artifact result, implementation summary, and limitations are in
HEADLESS_BRIDGE_FINAL_REPORT.md.

## Evidence limits

- No physical controller was available. Physical button, stick, trigger, and
  D-pad behavior remains community-test-needed; replay fixtures validate only
  the raw-report-to-teaching software path.
- The Windows VM was unavailable over SSH, and the reachable Proxmox inventory
  contained no Windows VM. A Windows CI runner executes the automated suite and
  headless self-test, but no physical HID device is attached there.
- The WPF interface was not interactively launched in this phase. Visual
  behavior remains interactive-test-needed and is outside the headless bridge
  acceptance gate.
- No physical model is claimed as maintainer-tested. Contribution evidence
  levels and report privacy expectations are in CONTRIBUTING.md and
  HARDWARE_CONTRIBUTION.md.

## Completion state

Alpha 0.1 history remains unchanged. The Headless Community Bridge is complete
when every item in HEADLESS_BRIDGE_ACCEPTANCE.md is verified. No AI integration,
console support, or unrelated phase work is included. No tag or GitHub Release
has been created.

The forward project direction remains documented in [VISION.md](VISION.md).
