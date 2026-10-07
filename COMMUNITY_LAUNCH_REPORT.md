# ControllerOS Community Launch Report

Date: 2026-10-07

## Published release

- Release: [ControllerOS 0.1 — Experimental Community Preview](https://github.com/TenchiNeko/ControllerOS/releases/tag/v0.1.0-alpha.1)
- Tag: `v0.1.0-alpha.1`
- Release commit: `3634d7ddbc2cfc01af79bd62678f633febd49d98`
- Primary package: `controlleros-0.1.0-alpha.1-win-x64.zip`
- Checksum file: `controlleros-0.1.0-alpha.1-win-x64.zip.sha256`
- ZIP SHA-256: `4351cf957688e0d78795c6dcdfcd601675febf54074e083b49d1f10919b7e6f3`

The release is a public GitHub prerelease. The ZIP and checksum sidecar are
attached to the release page, and both public asset URLs returned HTTP 200.
The release tag resolves to the exact commit shown above. No version tag or
release was overwritten.

## Validation evidence

- CI run [37672386842](https://github.com/TenchiNeko/ControllerOS/actions/runs/37672386842)
  passed on the exact release commit. The Windows smoke and Linux build jobs
  passed, including Release build, 48/48 tests, formatting verification,
  ControllerScript and example checks, self-test, and Windows package upload.
- The self-contained package was downloaded from the public release and its
  SHA-256 matched the published sidecar. Its embedded manifest verified all
  204 files; the ZIP contains 205 entries including that manifest.
- The package includes the Windows x64 CLI and runtime, first-use guide,
  ControllerScript documentation, two profile examples, Apache-2.0 license,
  dependency/license notices, build identity, release notes, and checksums.
  No PDBs or matches for credential markers, private build paths, or host
  details were found in the package scan.
- On Windows VM 101, the packaged executable launched without an installed
  .NET SDK. `--help`, `version --json`, `self-test --json` (5/5 checks),
  `devices --json`, starter-profile validation, and starter-profile simulation
  passed. The CLI reported version `0.1.0-alpha.1` and the exact release
  commit. Device enumeration found no physical gamepad attached.
- An independent review of packaging, documentation, command syntax,
  dependency notices, privacy boundaries, and evidence claims found no
  release-blocking issues.
- Repository governance and permissions remained in place. The release
  preparation was merged through PR #4 with the required CI checks; main
  protection and owner recovery bypass were not changed.

## Known limitations

- The maintainer did not have a physical controller for this launch. Physical
  button, stick, trigger, hat, and connection-mode behavior needs community
  testing. Synthetic and replay tests validate software behavior only.
- VM 101 had no physical controller; the device-list command was verified with
  zero devices. No physical compatibility claim is made.
- The WPF desktop interface was not interactively tested and is not included
  in this CLI package.
- Proprietary paddles, gyro, adaptive triggers, touchpads, LEDs, speakers, and
  other special functions are unsupported unless an observable interface is
  understood by ControllerOS. Console support is not included. Some devices
  may need definitions or protocol support.

## First community contribution opportunities

1. Test a controller over USB, Bluetooth, or its receiver and state which
   connection mode was used. Testing the same model in more than one mode is
   useful because Windows may expose different controls for each.
2. Record which standard buttons, stick axes, triggers, and D-pad/hat controls
   were detected, worked, or failed; report calibration or disconnect problems
   with reproduction steps.
3. Export the sanitized hardware report, inspect it before attaching it, and
   choose the evidence level that matches the test. Never attach a report or
   screenshot without reviewing it for personal or device-identifying data.
4. Submit findings using the concise
   [Controller Compatibility Report form](.github/ISSUE_TEMPLATE/hardware-report.yml).
   General problems and suggestions can use the
   [Bug Report](.github/ISSUE_TEMPLATE/bug_report.yml) and
   [Feature Request](.github/ISSUE_TEMPLATE/feature_request.yml) forms.

See [HARDWARE_CONTRIBUTION.md](HARDWARE_CONTRIBUTION.md) for download, first
use, troubleshooting, report inspection, and submission instructions.
