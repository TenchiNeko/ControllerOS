# ControllerOS Project Status

Last updated: 2026-10-07

## Release target

**Alpha 0.1 — implementation complete; public release not published.**

The authoritative scope and release gates are README.md, ARCHITECTURE.md,
CONTROLLERSCRIPT.md, ROADMAP.md, ACCEPTANCE.md, and AGENTS.md.

## Milestones

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

## Validation commands

Run from the repository root with the .NET 10 SDK:

```sh
dotnet restore ControllerOS.sln
dotnet build ControllerOS.sln --configuration Release --no-restore
dotnet test ControllerOS.sln --configuration Release --no-build
dotnet format ControllerOS.sln --verify-no-changes --no-restore
```

Release build and format verification pass with zero warnings/errors; the
automated suite passes 36/36 tests. The manual Windows adapter check and its
results are recorded in FINAL_REPORT.md.

## Physical and interactive validation gaps

- No physical gamepad has been tested. The Windows VM check exercised XInput
  using ControllerOS's synthetic virtual output, without GPU passthrough.
- Windows HID enumeration is read-only. The desktop teaching flow currently
  consumes the built-in synthetic fixture; it cannot collect raw HID samples
  from a discovered physical device.
- XInput polling does not expose GUIDE, so the XInput source omits that
  capability. Keyboard test input includes GUIDE.
- The Windows VM had no interactive user session (`quser` reported none), so the
  WPF window was cross-built but not launched for a visual GUI check. The
  compile-before-stop path is covered by code review and the Core failure
  isolation test, not an interactive bad-edit UI test.
- No physical controller model is claimed as supported. Contributors can
  provide physical mapping evidence using the levels and privacy guidance in
  CONTRIBUTING.md.

## Completion state

All Alpha 0.1 milestones are implemented and the required repository validation
passes. Physical controller teaching and interactive WPF launch remain the
documented limitations above. No public tag or release was created.

The final review evidence, commands, implementation summary, and next community
contributions are in FINAL_REPORT.md. Stop after final commit; do not begin
Alpha 0.2 work.


## Forward direction

Alpha 0.1 remains complete and is preserved as the initial architectural proof.

The forward project vision is now documented in [VISION.md](VISION.md). The
project is **headless-first**: future release-critical capabilities should be
available through reusable core services and CLI/API surfaces rather than
requiring the original maintainer to operate an interactive desktop.

Highest-value next work, without changing Alpha 0.1 history:

1. expose the existing core through a headless CLI;
2. capture raw input from physical HID devices;
3. connect that capture to the existing teaching/calibration engine;
4. support headless sanitized hardware-report export;
5. produce automated Windows artifacts for community testers;
6. let physical hardware owners supply validation evidence and mappings.

No physical controller or graphical desktop is required for the original
maintainer to continue architecture, runtime, CI, and review work. Hardware
support must remain explicitly evidence-based.
