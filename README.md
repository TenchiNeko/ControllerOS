# ControllerOS

**A headless-first programmable controller platform with a hardware-independent core.**

ControllerOS is designed so the core can be built, tested, configured, and
maintained remotely without requiring the maintainer to sit at a Windows
desktop or physically own every controller. CLI/API automation, synthetic
fixtures, and community-supplied hardware evidence are first-class parts of
the long-term architecture; the WPF application is an optional frontend.

ControllerOS Alpha 0.1 is the first architectural proof: a Windows-capable
runtime for mapping normalized controller input through a restricted script
runtime and sending output to a debug sink or an Xbox-style virtual controller.

See [VISION.md](VISION.md) for the headless-first, community-hardware direction.

The alpha includes:

- a portable model for standard buttons, axes, and triggers
- deterministic synthetic input, recorded output, and timestamped scenarios
- a sandboxed ControllerScript compiler and runtime with yielding timers
- keyboard test input and Windows HID enumeration
- versioned profile and device-definition JSON with local calibration storage
- a teaching and report workflow exercised with a synthetic unknown device
- a Windows virtual Xbox output adapter and desktop editor/runtime

## Controller path

```text
physical / keyboard / synthetic input
                   ↓
          normalized controller state
                   ↓
       ControllerScript runtime and timers
                   ↓
             normalized output
                   ↓
    debug preview / virtual controller
```

The intended frontend model is:

```text
CLI  ───────┐
Desktop UI ─┼→ same ControllerOS core/runtime
AI/API ─────┘
```

Future physical-device teaching should also be available headlessly so a
hardware owner can generate a validated, privacy-bounded contribution report
without requiring a graphical session.

Windows HID enumeration lists interfaces and matches device definitions. The
teaching screen currently uses synthetic raw samples; it does not collect raw
reports from a physical HID device. The desktop's normalized input inspector
shows the active test or Windows XInput source.

## ControllerScript

ControllerScript v0.1 supports `press`, `release`, and `change` handlers;
state, conditions, non-recursive functions, button output calls, normalized
output writes, and non-blocking waits.

```python
on press(SOUTH):
    press(EAST)
    wait(100ms)
    release(EAST)

on change(RIGHT_X):
    output.RIGHT_X = RIGHT_X * 1.35
```

It has no loops, layers, imports, collections, or host-computer APIs. See
[CONTROLLERSCRIPT.md](CONTROLLERSCRIPT.md) for the supported syntax and limits.

## Devices and community reports

The device-definition format separates reusable raw-to-standard mappings from
per-unit calibration. The desktop can enumerate Windows HID interfaces, show
known/unknown status when identifiers match a local definition, and run the
teaching steps against the built-in synthetic unknown-device fixture. Reports
contain only allowlisted device, capability, mapping, calibration, version, and
validation fields. Raw control labels are replaced with anonymous ordinal IDs
in exported reports. Review a report before sharing it.
The exact serialized v2 property names, types, enum values, and validation
rules are in [HARDWARE_REPORT.md](HARDWARE_REPORT.md).

No physical controller model is claimed as supported in this alpha. Hardware
support evidence must use the levels in [CONTRIBUTING.md](CONTRIBUTING.md).

## Windows desktop

The desktop includes profile JSON load/save, source editing, compile/runtime
diagnostics, keyboard or XInput input, live normalized input/output views,
teaching, report export, start/stop, and an emergency disable control. The
keyboard map is shown in the Devices tab: WASD controls the left stick, arrow
keys control the right stick, Space/E/Q/R are SOUTH/EAST/WEST/NORTH, U/I are
bumpers, Shift keys are triggers, Enter/Tab/F1 are MENU/VIEW/GUIDE, Ctrl keys
click the sticks, and NumPad 8/2/4/6 drive the D-pad. Escape disables a running
profile and releases outputs.

The virtual Xbox adapter uses HIDMaestro v1.11.0. If missing, the backend shows
an actionable error and the application continues with debug preview.

## Build and validation

Install the .NET 10 SDK. From the repository root, run:

```sh
dotnet restore ControllerOS.sln
dotnet build ControllerOS.sln --configuration Release --no-restore
dotnet test ControllerOS.sln --configuration Release --no-build
dotnet format ControllerOS.sln --verify-no-changes --no-restore
```

The solution cross-builds its Windows-targeted projects from Linux; launching
the WPF desktop requires Windows. The first build downloads the pinned
HIDMaestro v1.11.0 archive from its [official release
page](https://github.com/hifihedgehog/HIDMaestro/releases/tag/v1.11.0) when
absent and verifies the archive and SDK DLL SHA-256 hashes. The app does not
download dependencies at runtime. Installing the virtual output driver may
require administrator rights.

Launch the desktop on Windows with:

```powershell
dotnet run --project src/ControllerOS.Desktop/ControllerOS.Desktop.csproj -c Release
```

The manual Windows integration check is in
`tests/ControllerOS.Windows.Integration/ControllerOS.Windows.Integration.csproj`.
Run it in an elevated Windows terminal:

```powershell
dotnet run --project tests/ControllerOS.Windows.Integration/ControllerOS.Windows.Integration.csproj -c Release
```

CI runs the build, automated tests, and format verification. The Windows
integration check installs/starts a virtual device and is kept manual.

## Scope and safety

Alpha 0.1 is Windows-first. It does not support consoles, every proprietary
controller feature, native profile plugins, or physical raw-HID teaching. It
does not include anti-cheat circumvention, detection evasion, hardware-identity
spoofing, process injection, game memory manipulation, or packet manipulation.

## Repository status

See [PROJECT_STATUS.md](PROJECT_STATUS.md), [FINAL_REPORT.md](FINAL_REPORT.md),
and [VISION.md](VISION.md) for milestone evidence, current limitations, and
the forward community-maintained direction. The contract and development files are
[ARCHITECTURE.md](ARCHITECTURE.md), [ROADMAP.md](ROADMAP.md),
[ACCEPTANCE.md](ACCEPTANCE.md), [HARDWARE_REPORT.md](HARDWARE_REPORT.md),
[CONTRIBUTING.md](CONTRIBUTING.md),
[AGENTS.md](AGENTS.md), and [CODEX_START.md](CODEX_START.md).

## License

ControllerOS is licensed under the [Apache License 2.0](LICENSE).
