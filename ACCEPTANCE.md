# Alpha 0.1 Acceptance Criteria

This file is the release gate. Checklist items are satisfied by the
implementation and evidence in FINAL_REPORT.md. Windows HID teaching remains
synthetic-only, and physical controller validation remains unverified; those
limits are documented in README.md, ARCHITECTURE.md, and PROJECT_STATUS.md.

## Build and repository

- [x] Clean checkout builds using documented commands.
- [x] Automated test suite runs using documented commands.
- [x] CI runs build, tests, and format/analyzer checks.
- [x] No committed secrets or machine-specific credentials.
- [x] No required dependency downloads at runtime; HIDMaestro is pinned and hash-verified at build time.

## Core model

- [x] Buttons, triggers, and axes have explicit normalized types/ranges.
- [x] ControllerState is hardware-independent.
- [x] OutputState is hardware-independent.
- [x] SyntheticInput and RecordedOutput exercise the core without hardware.

## ControllerScript

- [x] Valid v0.1 source parses and compiles.
- [x] Invalid syntax reports useful source locations.
- [x] Invalid controls/types are rejected; runtime construction checks the selected input capabilities.
- [x] State variables work.
- [x] Conditions work.
- [x] Non-recursive functions work.
- [x] press/release/tap work.
- [x] wait yields rather than blocking input processing, including while idle.
- [x] Runtime instruction budget is enforced.
- [x] Runtime timer/task quotas are enforced.
- [x] A failing handler/profile cannot terminate the core service.
- [x] Normal profiles have no filesystem/network/process/native APIs.

## Timing and determinism

- [x] Synthetic timestamped scenarios are replayable.
- [x] The same input scenario and profile produce the same logical output trace.
- [x] Same-time ordering is documented and tested.
- [x] Pending output commits do not expose intermediate handler states.

## Devices

- [x] Windows HID enumeration and XInput polling are isolated behind the Windows adapter.
- [x] Known definitions can be loaded and validated.
- [x] An unknown synthetic device enters the teaching flow; physical raw-HID teaching is not implemented.
- [x] Global mapping and per-unit calibration are stored separately.
- [x] Unsupported controls can be skipped without invalidating the remaining mapping.

## Teaching/calibration

- [x] Button discovery works against synthetic raw controls.
- [x] Stick-axis discovery works against synthetic movement.
- [x] Trigger/range discovery works against synthetic values.
- [x] Center/noise samples can be captured.
- [x] The mapping is previewed before save.
- [x] Saved mappings are recognized after reload.

## Community report

- [x] A report is generated from a synthetic unknown-device session.
- [x] Report schema v2's serialized shape, types, enums, and validation are documented in HARDWARE_REPORT.md.
- [x] Privacy-sensitive fields are allowlisted, raw labels are anonymized, and serialization rejects caller-supplied non-anonymous IDs.
- [x] Tests verify host/user-like labels, unrelated inventory fields, paths, and address-like labels are absent.
- [x] Reports include mapping and calibration evidence for a conventional definition.

## Virtual output

- [x] Output backend is behind an interface.
- [x] Alpha includes an Xbox-style Windows virtual output implementation.
- [x] Missing driver/backend state produces an actionable error.
- [x] No anti-cheat probing, game-specific evasion, HWID spoofing, or ban-evasion logic is included.

## Desktop Alpha

- [x] User can see enumerated devices and known/unknown definition status.
- [x] User can inspect live keyboard or XInput normalized values.
- [x] User can run the synthetic teaching/calibration workflow.
- [x] User can edit, load, and save a ControllerScript profile.
- [x] Compile/runtime errors are visible.
- [x] User can start/stop a profile.
- [x] User can disable a profile through a recovery control.
- [x] User can export the synthetic teaching report.

## Documentation

- [x] README describes implemented behavior and limitations.
- [x] Architecture docs match the adapters and runtime.
- [x] ControllerScript v0.1 docs match the parser/runtime.
- [x] Contribution and build workflows were checked from a clean checkout.
- [x] Hardware support evidence levels and claims are explicit.

## Final review

- [x] Independent adversarial review completed.
- [x] P0/P1 findings were resolved; remaining physical and interactive validation limits are documented.
- [x] FINAL_REPORT.md is committed.
- [x] No public tag or release was created before the acceptance gate; no remote release was published as part of this implementation task.
