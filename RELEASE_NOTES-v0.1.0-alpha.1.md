# ControllerOS 0.1 — Experimental Community Preview

**ControllerOS v0.1.0-alpha.1 is an experimental release.** It is a
programmable controller platform for scripting behavior, calibrating exposed
controls, discovering HID devices, and sharing hardware findings. Physical
controller compatibility needs community validation; universal support is not
claimed.

## Included

- Headless Windows ControllerOS CLI in a self-contained Windows x64 ZIP.
- Sandboxed ControllerScript compiler/runtime and normalized controller input.
- Windows HID discovery and selected-device raw input capture.
- Guided teaching and calibration for observable buttons, sticks, triggers,
  and supported D-pad/hat controls.
- Virtual Xbox output through the pinned HIDMaestro dependency.
- Synthetic input, replay tests, and a headless self-test.
- Allowlisted community hardware reports and contribution templates.
- Build version, exact commit, license notices, examples, and SHA-256 checksums
  in the Windows package.

## Known limitations

- The maintainer has not physically validated a controller. Physical behavior
  is **community-test-needed**.
- Proprietary paddles, gyro, adaptive triggers, touchpads, LEDs, speakers, and
  other special functions are unsupported unless the device exposes evidence
  that ControllerOS understands.
- The WPF graphical interface has not been interactively tested and is not in
  the downloadable package.
- Console support is not included.
- Some controllers may need additional definitions or protocol support.
- Windows may expose different controls over USB, Bluetooth, or a vendor
  dongle; each connection mode needs its own evidence.

## Help test a controller

Download the Windows ZIP and adjacent `.sha256` file from this release, verify
the checksum, and follow [HARDWARE_CONTRIBUTION.md](HARDWARE_CONTRIBUTION.md).
Review the sanitized report and any attachment before submitting it with the
Controller Compatibility Report issue form. Mark evidence as
**contributor-tested**, **mapping-only**, or **unverified** accurately.
