# ControllerOS Alpha 0.1

## Included

- Normalized standard buttons, sticks, and triggers with synthetic and keyboard input.
- Windows HID enumeration and XInput polling for controller input.
- A sandboxed ControllerScript v0.1 compiler/runtime with state, handlers,
  functions, deterministic output commits, and non-blocking waits.
- Versioned profile, device-definition, and per-unit calibration JSON.
- Synthetic unknown-device teaching, mapping preview/save, and privacy-allowlisted
  hardware reports.
- A WPF desktop editor, diagnostics, live input/output inspectors, and emergency
  profile disable.
- A Windows Xbox-style virtual output backend using HIDMaestro v1.11.0.

## Validation and limitations

The release candidate is built and exercised in automated tests and a Windows
11 VM. The VM integration check observes the synthetic virtual output through
XInput; it does not validate a physical gamepad. The teaching UI currently uses
synthetic raw samples and cannot teach a physical HID device. XInput does not
expose the GUIDE control through its documented polling API. No physical
controller model is claimed as supported.

The Windows VM had no interactive user session available for launching and
visually checking the WPF window. The desktop project cross-builds successfully;
the Windows adapter and virtual output paths have separate console integration
coverage.
