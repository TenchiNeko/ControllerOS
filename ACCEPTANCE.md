# Alpha 0.1 Acceptance Criteria

This file is the release gate.

## Build and repository

- [ ] Clean checkout builds using documented commands.
- [ ] Automated test suite runs using documented commands.
- [ ] CI runs build + tests + formatting/lint checks.
- [ ] No committed secrets or machine-specific credentials.
- [ ] No required dependency is silently downloaded from an untrusted ad-hoc URL at runtime.

## Core model

- [ ] Buttons, triggers, and axes have explicit normalized types/ranges.
- [ ] ControllerState is hardware-independent.
- [ ] OutputState is hardware-independent.
- [ ] SyntheticInput and RecordedOutput exercise the full core without hardware.

## ControllerScript

- [ ] Valid v0.1 source parses and compiles.
- [ ] Invalid syntax reports useful source locations.
- [ ] Invalid controller types/controls are rejected.
- [ ] State variables work.
- [ ] Conditions work.
- [ ] Non-recursive functions work.
- [ ] press/release/tap work.
- [ ] wait yields rather than blocking input processing.
- [ ] Runtime instruction budget is enforced.
- [ ] Runtime timer/task quotas are enforced.
- [ ] One failing profile/handler cannot terminate the core service.
- [ ] Normal profiles cannot access filesystem/network/process/native APIs.

## Timing and determinism

- [ ] Synthetic timestamped scenarios are replayable.
- [ ] Same input scenario + same profile produces the same logical output trace.
- [ ] Simultaneous/same-time scheduler ordering is documented and tested.
- [ ] Pending output commits cannot expose undocumented intermediate states.

## Devices

- [ ] Windows device enumeration is isolated behind an adapter.
- [ ] Known definitions can be loaded/validated.
- [ ] Unknown devices enter teaching flow.
- [ ] Global mapping and per-unit calibration are stored separately.
- [ ] Unsupported controls can be skipped without invalidating the entire device.

## Teaching/calibration

- [ ] Button discovery works against synthetic raw controls.
- [ ] Stick-axis discovery works against synthetic movement.
- [ ] Trigger/range discovery works against synthetic values.
- [ ] Center/noise samples can be captured.
- [ ] Resulting mapping is previewed before save.
- [ ] Saved mapping is recognized on subsequent load.

## Community report

- [ ] Report is generated from an unknown-device session.
- [ ] Report schema is documented.
- [ ] Privacy-sensitive fields are allowlisted, not dumped broadly.
- [ ] Tests verify usernames, hostnames, unrelated device inventory, IP addresses, and arbitrary paths are absent.
- [ ] Report includes enough mapping evidence to reproduce/validate a conventional controller definition where possible.

## Virtual output

- [ ] Output backend is behind an interface.
- [ ] Alpha includes one Xbox-style Windows virtual output implementation.
- [ ] Missing driver/backend state produces an actionable error.
- [ ] No anti-cheat probing, game-specific evasion, HWID spoofing, or ban-evasion logic is included.

## Desktop Alpha

- [ ] User can see discovered devices.
- [ ] User can inspect live normalized inputs.
- [ ] User can run teaching/calibration.
- [ ] User can edit/load a ControllerScript profile.
- [ ] Compile/runtime errors are visible.
- [ ] User can start/stop a profile.
- [ ] User can disable an active profile through a recovery control.
- [ ] User can export a hardware report.

## Documentation

- [ ] README describes actual behavior, not aspirational features as completed features.
- [ ] Architecture docs match implementation.
- [ ] ControllerScript v0.1 docs match parser/runtime.
- [ ] Contribution workflow is tested by a maintainer from a clean checkout.
- [ ] Hardware support claims state evidence source: maintainer-tested, contributor-tested, mapping-only, or unverified.

## Final review

- [ ] Independent review completed.
- [ ] P0/P1 findings resolved.
- [ ] FINAL_REPORT.md committed.
- [ ] Alpha 0.1 tagged/released only after the above gates are satisfied.
