# Headless Community Bridge phase report

**Status: complete.** The Headless Community Bridge merged to `main` through
PR [#1](https://github.com/TenchiNeko/ControllerOS/pull/1), and the post-merge
commit and CI run were verified.

## Source and governance

- Base: public `main` at `a126a250e79411ca3fb8e6ceb55febaa2bf5a8b5`.
- PR head: `aaaf2fba6e811050d5434337ac02068a5d0ef8e0`.
- Squash merge: `1d3d3f78475dc2610574dd1dc94260cf4cfcbbd4`.
- `origin/main` was verified at the squash merge commit. Alpha 0.1 history is
  unchanged. No tag or GitHub Release was created.
- Live repository settings confirm public visibility, `main` as default,
  squash/rebase enabled and merge commits disabled. Active ruleset
  `main-protection` targets `main`, prevents deletion and force-push, requires
  resolved review conversations and the exact `build-test-format` check, and
  requires zero approvals. Owner/admin user id `256011908` retains `always`
  bypass and the API reports `current_user_can_bypass=always`.
- Actions are enabled with read-only default workflow token permissions; the
  workflows cannot approve pull requests. No repository administration setting
  remained inaccessible.

## Delivered implementation

- Added a headless Windows CLI over existing ControllerOS services for device
  listing and inspection, guided teaching, validation, simulation, report
  export, runtime start/stop, and self-test.
- Added selected-interface Windows HID capture with bounded reports,
  cancellation/disconnection handling, scalar controls, and supported D-pad
  hats. Raw observations use the existing validation and teaching engine.
- Added separate per-unit local calibration and strict allowlisted report
  schema v3, privacy tests, contribution instructions, and CI packaging for a
  self-contained Windows CLI artifact.
- Added an idle runtime input pump so ControllerScript timers progress while a
  change-only HID stream is quiet.

## Validation

- Detached clean checkout at
  `deb47eda637bb67bb34b9a9d8719acf171125bc3`: Release build passed with zero
  warnings/errors, 47/47 tests passed, and format verification passed.
- Linux `controlleros self-test --json`: 5/5 applicable checks passed.
- Self-contained Windows CLI from the reviewed implementation commit passed
  hash verification for 200 files and self-test 5/5 on VM 101.
- VM 101 Windows integration opened the virtual Xbox HID interface, enumerated
  14 controls, canceled a pending idle read, verified XInput button/trigger/
  stick normalization, observed disconnect neutralization, and confirmed
  interface removal.
- Independent review found and verified fixes for multi-usage HID ranges and
  accurate cancellation evidence; no P0/P1 issue remained. Repeated values for
  one usage remain unsupported pending `HidP_GetUsageValueArray`, consistent
  with [Microsoft's HID capability documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/value-capability-arrays).
- PR run `37665751598` and post-merge main run `37666364932` passed both
  `windows-cli-smoke` and `build-test-format`. The main run restored, built,
  tested, formatted, published, and uploaded the Windows artifact.
- Downloaded main artifact
  `controlleros-experimental-win-x64-1d3d3f78475dc2610574dd1dc94260cf4cfcbbd4`;
  all 200 files passed SHA-256 verification, and `BUILD-INFO.txt` matched the
  exact main commit. The package contains the pinned output dependency and
  license notices.

## Evidence limits

- No physical controller was available. Physical button, stick, trigger, and
  D-pad capture/teaching remains **community-test-needed**. VM 101 had no generic
  gamepad or joystick collection; its integration probe used the virtual Xbox
  device and did not claim physical-device validation.
- The WPF UI was not launched interactively; graphical behavior remains
  **interactive-test-needed**.
- No physical model is claimed as maintainer-tested. Contribution reports
  distinguish maintainer-tested, contributor-tested, mapping-only, and
  unverified evidence.

No manual GitHub administration action remains. The OAuth HTTPS token still
lacks `workflow`, so the authorized existing SSH key was used for the Git push;
GitHub CLI/API administration access remained available and the configured
governance rules were enforced during the PR merge.
