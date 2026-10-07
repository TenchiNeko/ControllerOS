# Headless Community Bridge phase report

**Status: implementation and local validation are complete; repository CI and
artifact acceptance are pending an authentication-scope update.** This phase
has not been marked complete or merged.

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
- Automated tests passed: 44/44.
- `dotnet format ControllerOS.sln --verify-no-changes --no-restore` passed.
- Linux `controlleros self-test --json` passed 5/5 applicable checks; Windows
  output and HID enumeration were correctly reported as not applicable.
- `git diff --check` passed.
- Independent review found no P0/P1 issues after the runtime and calibration
  fixes.

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
- The Windows VM was unavailable over SSH and no physical controller was
  available. Native HID parsing on physical devices remains
  **community-test-needed**; replay fixtures verify deterministic software
  behavior but do not claim physical-device validation.
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
