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

Alpha 0.1 remains the first architectural proof. The current Headless Community
Bridge adds:

- a headless Windows CLI over the existing core/runtime
- selected-device raw Windows HID capture for supported button and scalar-value controls
- guided physical teaching through the existing calibration engine
- privacy-bounded version 3 reports and separate per-unit calibration
- replay fixtures, privacy tests, self-test, and CI-built Windows artifacts

The original Alpha 0.1 milestones and report remain historical evidence; they
are not rewritten by this phase.

## Controller path

```text
physical controller → selected raw HID capture → headless CLI
                                      ↓
                         existing teaching/calibration engine
                                      ↓
                         validated local device mapping
                                      ↓
                         privacy-bounded community report

keyboard / XInput / synthetic input → normalized controller state
                                      ↓
                         ControllerScript runtime and timers
                                      ↓
                         debug preview / virtual controller
```

The intended frontend model is:

```text
CLI  ───────┐
Desktop UI ─┼→ same ControllerOS core/runtime
AI/API ─────┘
```

The WPF desktop remains an optional frontend. The CLI supports remote and SSH
use without an interactive desktop. Generic HID exposes raw buttons, scalar
values, and supported D-pad hats; proprietary or array-based controls are
reported as unavailable unless ControllerOS has evidence it understands.

## Headless CLI

The Windows CLI is named `controlleros.exe`. Run `controlleros --help` for the
full syntax. Typical commands are:

```powershell
.\controlleros.exe self-test --json
.\controlleros.exe devices
.\controlleros.exe inspect controller-001
.\controlleros.exe teach controller-001
.\controlleros.exe validate .\profile.json
.\controlleros.exe simulate .\profile.json --json
.\controlleros.exe export-report
```

`devices` lists only generic HID gamepad/joystick collections. Device IDs are
temporary indexes from the current enumeration. `teach` prompts for each
standard control, supports `skip`, previews the mapping before saving, and
stores each unit's calibration separately. `runtime start` requires a profile,
device ID, definition ID, and calibration ID; `runtime stop` sends a same-user
local stop request. Non-interactive commands support `--json`. Exit codes are
stable: 0 success, 1 operational failure, 2 usage error, 3 invalid input,
4 unsupported platform, and 130 cancellation.

See [HARDWARE_CONTRIBUTION.md](HARDWARE_CONTRIBUTION.md) for the non-programmer
artifact and report workflow.

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
per-unit calibration. The headless CLI captures one selected Windows HID
interface and feeds observations into the existing teaching session. Reports
contain only allowlisted device, capability, mapping, calibration, version,
commit, evidence, and validation fields. Raw control labels are replaced with
anonymous ordinal IDs in exported reports. Review a report before sharing it.
The exact serialized v3 property names, types, enum values, and validation
rules are in [HARDWARE_REPORT.md](HARDWARE_REPORT.md).

No physical controller model is claimed as maintainer-tested. Replay fixtures
validate the software pipeline, but physical-device mapping remains
community-test-needed. Hardware evidence levels are in
[CONTRIBUTING.md](CONTRIBUTING.md).

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

CI runs the automated suite and headless self-test on Windows, then runs the
Release build, tests, format verification, and a self-contained `win-x64` CLI
publish. The published experimental artifact
includes build commit/version, dependency notices, and a SHA-256 manifest. It is
downloaded from the successful workflow run's Artifacts section, not a Release.
The Windows integration check starts a virtual controller and exercises XInput
and selected-device raw HID capture; it remains a manual VM check.

## Scope and safety

The current phase is Windows-first. It does not support consoles, every
proprietary controller feature, or native profile plugins. Physical behavior
still needs contributor validation. It
does not include anti-cheat circumvention, detection evasion, hardware-identity
spoofing, process injection, game memory manipulation, or packet manipulation.

## Repository status

See [PROJECT_STATUS.md](PROJECT_STATUS.md), [FINAL_REPORT.md](FINAL_REPORT.md),
and [HEADLESS_BRIDGE_FINAL_REPORT.md](HEADLESS_BRIDGE_FINAL_REPORT.md) for
phase evidence and current validation gaps. The contract and development files are
[ARCHITECTURE.md](ARCHITECTURE.md), [ROADMAP.md](ROADMAP.md),
[ACCEPTANCE.md](ACCEPTANCE.md), [HARDWARE_REPORT.md](HARDWARE_REPORT.md),
[HARDWARE_CONTRIBUTION.md](HARDWARE_CONTRIBUTION.md),
[HEADLESS_BRIDGE_ACCEPTANCE.md](HEADLESS_BRIDGE_ACCEPTANCE.md),
[GOVERNANCE.md](GOVERNANCE.md), [CONTRIBUTING.md](CONTRIBUTING.md),
[AGENTS.md](AGENTS.md), and [CODEX_START.md](CODEX_START.md).

## License

ControllerOS is licensed under the [Apache License 2.0](LICENSE).
