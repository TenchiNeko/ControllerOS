# ControllerOS Alpha 0.1 final report

**Result: Alpha 0.1 completion gates are satisfied.** The Windows-first
runtime, profile editor, normalized keyboard/XInput paths, synthetic teaching
flow, virtual Xbox output, and community report format are implemented. This
report distinguishes software and VM evidence from physical-controller and
interactive-desktop evidence that was unavailable.

## Delivered scope

- Hardware-independent normalized input/output models, timestamped synthetic
  scenarios, and deterministic runtime scheduling, including idle timer
  advancement and input-first ordering at equal deadlines.
- A restricted ControllerScript v0.1 compiler and bytecode runtime with
  profile validation, control-capability checks, task/timer/instruction
  limits, diagnostics, and safe output reset behavior.
- Versioned profile, reusable device-definition, per-unit calibration, and
  hardware-report formats with bounded parsing and validation.
- Windows HID enumeration and native XInput polling, keyboard test input, a
  WPF editor/runtime, and a pinned, hash-checked HIDMaestro virtual Xbox
  output adapter.
- A synthetic unknown-device teaching workflow and report exporter. Export
  validation requires anonymous, contiguous per-kind control ordinals and
  checks that evidence and calibration agree with the mapping candidate.

The precise implemented language and hardware-report JSON contract are in
[CONTROLLERSCRIPT.md](CONTROLLERSCRIPT.md) and
[HARDWARE_REPORT.md](HARDWARE_REPORT.md). Alpha scope and limitations are in
[README.md](README.md), [ARCHITECTURE.md](ARCHITECTURE.md), and
[PROJECT_STATUS.md](PROJECT_STATUS.md).

## Repository validation

The documented .NET 10 commands were run from a clean checkout of the final
commit:

```sh
dotnet restore ControllerOS.sln
dotnet build ControllerOS.sln --configuration Release --no-restore
dotnet test ControllerOS.sln --configuration Release --no-build
dotnet format ControllerOS.sln --verify-no-changes --no-restore
```

The Release build completed with zero warnings and zero errors. All 36 tests
passed. Format verification and `git diff --check` passed. The clean-checkout
run is recorded by the final repository commit and worktree validation.

## Windows VM evidence

The existing Windows VM was used for Windows-specific integration without a
physical controller or GPU passthrough. On Windows 11 Enterprise build 26200
with .NET 10.0.12, the manual integration program:

- discovered the virtual Xbox HID interface (VID `045E`, PID `028E`);
- read an EAST button press, left trigger value `0.75`, and left stick X value
  `0.50` through XInput;
- observed neutral releases with increasing timestamps after virtual-device
  disconnect, then disposed the interface; and
- produced the expected actionable diagnostic when the copied
  `HIDMaestro.Core.dll` dependency was absent.

The Windows-targeted desktop and integration projects Release-build
successfully. The VM had no interactive user session (`quser` reported none),
so WPF was not launched for a visual check. The test path uses ControllerOS's
virtual output as XInput input; it is not physical-controller validation.

## Review and fixes

Independent core and product reviews found no unresolved P0 or P1 issue. The
review cycle led to fixes for timer advancement while input is idle, preserving
the running profile when edited source fails compilation, Windows XInput
selection and capability validation, unsupported GUIDE advertisement,
anonymous-ID validation at report serialization, calibration-to-mapping range
checks, calibration sample-count bounds, and ControllerScript short-circuit
documentation. Regression assertions cover the report privacy boundary,
invalid calibration saves, compile-failure isolation, and timer ordering.

No required Alpha 0.1 TODO/FIXME remains in implementation sources. The final
review and clean-checkout validation found no new release blocker.

## Limitations and community opportunities

- Teaching currently uses the built-in synthetic raw-device fixture. Windows
  HID enumeration is read-only; raw report capture from a discovered physical
  device is not implemented.
- No physical controller was tested, so no physical model is claimed as
  supported. XInput does not expose GUIDE, and that source correctly omits the
  capability.
- The WPF visual workflow still needs a manual smoke check from an interactive
  Windows session.

The most useful next community contributions are physical-device mapping
evidence across controller models, validation of calibration against real
hardware, and an interactive Windows desktop smoke report. Follow the evidence
levels and privacy guidance in [CONTRIBUTING.md](CONTRIBUTING.md); do not include
serial numbers, user paths, host names, or unrelated device inventory in a
report.

No public tag or release was created, and no remote release was published.
