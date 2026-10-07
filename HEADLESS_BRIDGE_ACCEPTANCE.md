# Headless Community Bridge Acceptance

This phase extends Alpha 0.1 without changing its acceptance history. The
software phase may complete without a physical controller; physical behavior
must remain clearly marked for community testing until real evidence exists.

## Acceptance ledger

| Requirement | Evidence | State |
| --- | --- | --- |
| Thin headless CLI reuses existing core, runtime, teaching, validation, and report services | `src/ControllerOS.Cli`; parser, invocation, exit-code, and idle-runtime-timer tests | Verified locally |
| Selected-device Windows HID capture is bounded, cancellable, and isolated behind the Windows adapter | `WindowsHidInputCapture`; SDK capability-layout assertions; Windows CI build and smoke job | Source/build verified; physical device unavailable |
| Teaching uses the existing engine for buttons, axes, triggers, hats, skip, calibration, preview, and local save | `DeviceTeachingSession`; replay-to-teaching tests; per-unit calibration is bound to its definition and local interface hash | Verified in deterministic fixtures |
| Replayable raw-HID observations exercise the shared capture-to-teaching pipeline | `RawHidInputPipelineTests` replay buttons, D-pad, signed axes, and trigger observations through the existing teaching engine | Verified in deterministic fixtures |
| Reports use an allowlist and exclude host identity, paths, serials, IP addresses, unrelated inventory, and credentials | `DeviceTeachingTests` and `RawHidInputPipelineTests` privacy assertions; strict report schema v3 | Verified locally |
| Windows experimental CLI artifact is built from source and contains build identity, dependency notices, and hashes | `.github/workflows/ci.yml`; clean checkout CI run uploads a self-contained `win-x64` artifact | Pending required PR CI |
| Headless self-test covers runtime/script, synthetic teaching, report validation, output diagnostics, and device enumeration | Linux self-test passed 5/5; Windows CI executes the JSON self-test | Linux verified; Windows CI pending |
| Release solution build, all tests, and formatting pass | Local Release build; `dotnet test`; `dotnet format --verify-no-changes`; required clean-checkout CI | Local verified; clean-checkout CI pending |
| No P0/P1 issue remains and an independent review is complete | Final independent core review confirmed the timer pump and local unit-calibration binding; no P0/P1 issues found | Verified |
| Documentation distinguishes maintainer-tested, contributor-tested, mapping-only, and unverified evidence | README, CONTRIBUTING, HARDWARE_REPORT, HARDWARE_CONTRIBUTION, and PROJECT_STATUS | Verified |

## Evidence limits and community opportunities

- No physical controller was available for this phase. Button, stick, trigger,
  and hat decoding on physical devices remains **community-test-needed**.
- The Windows VM was unavailable over SSH; the reachable Proxmox inventory had
  no Windows VM. Windows-specific compilation and automated self-test run in
  GitHub Actions. No physical HID behavior is inferred from that run.
- The desktop UI was not launched interactively. Graphical behavior remains
  **interactive-test-needed** and is outside this headless phase's acceptance
  gate.
- Replay fixtures exercise the common raw-report validation boundary and
  teaching engine. They do not claim to replace testing Windows `HidP` parsing
  against physical report descriptors.

The phase is complete after the required PR CI and final independent review
pass, all implementation and documentation changes are committed through the
repository PR model, and remote `main` is verified at the merged commit. No
tag or GitHub Release is part of this acceptance document.
