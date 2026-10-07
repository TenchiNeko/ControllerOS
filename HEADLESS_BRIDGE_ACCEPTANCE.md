# Headless Community Bridge Acceptance

This phase extends Alpha 0.1 without changing its acceptance history. The
software phase may complete without a physical controller; physical behavior
must remain clearly marked for community testing until real evidence exists.

## Acceptance ledger

| Requirement | Evidence | State |
| --- | --- | --- |
| Thin headless CLI reuses existing core, runtime, teaching, validation, and report services | `src/ControllerOS.Cli`; parser, invocation, exit-code, and idle-runtime-timer tests | Verified locally |
| Selected-device Windows HID capture is bounded, cancellable, and isolated behind the Windows adapter | `WindowsHidInputCapture`; SDK capability-layout and multi-usage range tests; VM 101 opened the virtual Xbox HID interface, enumerated 14 controls, and canceled an idle read | Windows adapter verified on VM 101; physical device unavailable |
| Teaching uses the existing engine for buttons, axes, triggers, hats, skip, calibration, preview, and local save | `DeviceTeachingSession`; replay-to-teaching tests; per-unit calibration is bound to its definition and local interface hash | Verified in deterministic fixtures |
| Replayable raw-HID observations exercise the shared capture-to-teaching pipeline | `RawHidInputPipelineTests` replay buttons, D-pad, signed axes, and trigger observations through the existing teaching engine | Verified in deterministic fixtures |
| Reports use an allowlist and exclude host identity, paths, serials, IP addresses, unrelated inventory, and credentials | `DeviceTeachingTests` and `RawHidInputPipelineTests` privacy assertions; strict report schema v3 | Verified locally |
| Windows experimental CLI artifact is built from source and contains build identity, dependency notices, and hashes | Main CI run `37666364932` uploaded a self-contained `win-x64` artifact from main commit `1d3d3f78475dc2610574dd1dc94260cf4cfcbbd4`; all 200 files and build identity verified | Verified in GitHub Actions and downloaded artifact |
| Headless self-test covers runtime/script, synthetic teaching, report validation, output diagnostics, and device enumeration | Linux and VM 101 self-tests passed 5/5; Windows smoke job passed on PR and main CI runs | Verified locally, on VM 101, and in CI |
| Release solution build, all tests, and formatting pass | Clean-checkout Release build; 47 tests; `dotnet format --verify-no-changes`; required PR check `build-test-format` passed | Verified locally and in required PR CI |
| No P0/P1 issue remains and an independent review is complete | Independent HID review found and then verified fixes for multi-usage ranges and truthful cancellation evidence; no remaining P0/P1 issues found | Verified |
| Documentation distinguishes maintainer-tested, contributor-tested, mapping-only, and unverified evidence | README, CONTRIBUTING, HARDWARE_REPORT, HARDWARE_CONTRIBUTION, and PROJECT_STATUS | Verified |

## Evidence limits and community opportunities

- No physical controller was available for this phase. Button, stick, trigger,
  and hat decoding on physical devices remains **community-test-needed**.
- VM 101 was reached through the documented Windows-worker connection. It had
  no generic gamepad or joystick collection before or after the virtual Xbox
  integration check. Windows `HidP` parsing and cancellation passed against the
  virtual interface; physical HID behavior remains **community-test-needed**.
- The desktop UI was not launched interactively. Graphical behavior remains
  **interactive-test-needed** and is outside this headless phase's acceptance
  gate.
- Replay fixtures exercise the common raw-report validation boundary and
  teaching engine. They do not claim to replace testing Windows `HidP` parsing
  against physical report descriptors.

The phase completed through PR #1, merged by squash. The final independent
review found no P0/P1 issue, required PR checks passed, and remote `main` was
verified at `1d3d3f78475dc2610574dd1dc94260cf4cfcbbd4`. No tag or GitHub Release
was created.
