# Headless Community Bridge phase report

**Status: implementation, local validation, and Windows VM integration are
complete; repository CI and artifact acceptance are pending an
authentication-scope update.** This phase has not been marked complete or
merged.

## Source and governance

- Base: public `main` at `a126a250e79411ca3fb8e6ceb55febaa2bf5a8b5`.
- Implementation commit: `4c3b60faba2cf6740d185e5acf4d732cf1dcce77` on
  `feature/headless-community-bridge`.
- Alpha 0.1 history is unchanged. No tag or GitHub Release was created.
- Governance settings were read from the live repository and configured as
  recorded in GOVERNANCE.md. The authenticated user retained owner/admin
  bypass access; ordinary main changes use the PR and required-check model.

## Delivered implementation

- Added a Windows headless CLI over existing ControllerOS services, including
  device listing/inspection, guided teaching, validation, simulation, report
  export, runtime start/stop, and self-test.
- Added selected-interface Windows HID report capture using Windows HID parser
  capabilities, bounded input, cancellation/disconnection handling, scalar
  controls, and supported eight-position D-pad hats.
- Routed replay and physical observations through the common raw-HID validation
  boundary and existing teaching/calibration engine. Local calibration records
  identify their mapping definition and bind to a local hash of the selected
  HID interface path. That hash and path are not included in community reports.
- Added strict allowlisted report schema v3, privacy assertions, contribution
  instructions, dependency/license/hash notes, and Windows artifact CI
  packaging configuration.
- Added an idle runtime input pump so ControllerScript timers advance while a
  change-only HID device is quiet. Regression coverage verifies a timed output
  is released while the input stream is idle.

## Local verification

- Release solution build succeeded with zero warnings and errors.
- Automated tests passed: 47/47, including multi-usage HID range expansion,
  repeated-usage array rejection, and capability expansion bounds.
- `dotnet format ControllerOS.sln --verify-no-changes --no-restore` passed.
- Linux `controlleros self-test --json` passed 5/5 applicable checks; Windows
  output and HID enumeration were correctly reported as not applicable.
- A self-contained `win-x64` CLI package passed hash-manifest verification and
  `controlleros self-test --json` passed 5/5 checks on VM 101.
- The self-contained Windows integration probe passed on VM 101: it opened the
  selected virtual Xbox HID interface, exposed 14 controls, canceled an idle
  input read, verified XInput button/trigger/stick normalization, observed
  disconnect neutralization, and confirmed virtual-interface removal.
- `git diff --check` passed.
- Independent review identified a HID multi-usage-range capability gap and an
  inaccurate cancellation success message. The parser now expands one scalar
  target for each reported usage, keeps repeated-usage value arrays explicitly
  unsupported, and bounds expansion to 128 fields. The VM probe now reports
  cancellation success only after observing cancellation. Follow-up review
  found no remaining P0/P1 issues.

## Pending checks and evidence limits

- The Windows smoke job, clean-checkout CI build, and uploaded artifact have not
  run for this implementation because the branch push was rejected before a
  PR could be created.
- GitHub rejected the push because the authenticated OAuth App token lacks the
  `workflow` scope required to create or update `.github/workflows/ci.yml`.
  The existing authentication reported `repo`, `read:org`, and `gist` scopes.
  No token was exposed, no alternate credential was used, and no bypass of the
  repository governance model was attempted. The remote feature branch remains
  at its earlier governance-documentation commit.
- VM 101 was reached through the documented Windows-worker route. It exposed
  no generic gamepad/joystick collection; the integration probe used only the
  Xbox virtual output device. Physical controller capture and teaching remain
  **community-test-needed**. Replay fixtures validate the deterministic
  raw-report-to-teaching path without claiming physical-device validation.
- The WPF UI was not interactively launched; visual behavior remains
  **interactive-test-needed**.

## Remaining completion steps

The authenticated GitHub CLI session must be refreshed by the repository owner
with the `workflow` scope. Once that external action is complete, push the
existing branch, create a PR, wait for the required `build-test-format` check
and Windows smoke/artifact job, update this report and PROJECT_STATUS.md with
the actual CI evidence, then merge through the configured PR rules. Verify the
resulting `origin/main` SHA and stop. Physical hardware remains a community
test opportunity and does not block software completion.
