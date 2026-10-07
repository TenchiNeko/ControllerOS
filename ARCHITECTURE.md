# ControllerOS Architecture

## Objective

ControllerOS is a headless-first programmable controller runtime with Windows as
the initial physical/virtual device platform. The architecture must keep
controller-independent logic isolated from physical hardware, frontends, and
virtual-output implementations.

The runtime must be operable and testable without a graphical session. CLI,
desktop, automation, and future AI/API frontends should all target the same core
contracts rather than duplicating controller logic.

The core invariant is:

```text
Input adapter
    ↓
Normalized ControllerState
    ↓
Profile / ControllerScript runtime
    ↓
Normalized OutputState
    ↓
Output adapter
```

Everything above and below the normalized state boundary is replaceable.

Frontends are also replaceable:

```text
CLI  ───────┐
Desktop UI ─┼→ ControllerOS application/core services
AI/API ─────┘
```

No release-critical controller behavior should permanently require manual GUI
interaction when it can be exposed through a headless command/API.

## Alpha implementation choice

For Alpha 0.1, prefer C# on a current supported .NET runtime to minimize Windows integration friction and maximize iteration speed.

Do not make the public profile format, ControllerScript syntax, bytecode, hardware-definition schema, or conformance fixtures depend on CLR-specific concepts. They are platform contracts, not implementation details.

## Components

### 1. Device discovery

The Windows adapter enumerates HID interfaces with SetupAPI/HidP and exposes
local device paths, manufacturer/product strings, vendor/product/revision and
interface data, and HID usage/report capabilities. The desktop checks exact
vendor/product/usage matches against local definitions and shows known/unknown
status. Device paths remain local and are not written to reports.

No game-specific process inspection belongs here.

### 2. Input adapters

Every input backend converts raw device behavior into the same normalized model.

Alpha adapters:

- SyntheticInput
- KeyboardTestInput
- WindowsControllerInput, using native XInput polling for slots 0–3

WindowsControllerInput normalizes XInput buttons, triggers, and stick axes and
emits releases when a slot disconnects. XInput does not expose GUIDE through
its documented polling API, so the adapter does not advertise that control.
The desktop can select keyboard test input or an XInput slot. HID enumeration
does not yet provide generic raw HID input or samples for teaching; the teaching
screen uses the synthetic unknown-device fixture. Physical controller support
and physical teaching remain unverified.

### 3. Normalized ControllerState

Initial semantic controls:

```text
SOUTH EAST WEST NORTH
LEFT_BUMPER RIGHT_BUMPER
LEFT_TRIGGER RIGHT_TRIGGER
LEFT_STICK_X LEFT_STICK_Y
RIGHT_STICK_X RIGHT_STICK_Y
LEFT_STICK_CLICK RIGHT_STICK_CLICK
DPAD_UP DPAD_DOWN DPAD_LEFT DPAD_RIGHT
MENU VIEW GUIDE
```

Button values are Boolean.

Trigger values are normalized to `0.0..1.0`.

Stick axes are normalized to `-1.0..1.0`.

Device-specific controls may exist as capabilities, but Alpha must not force every unusual control into the standard set.

### 4. Device definitions

Conventional hardware support should be data-driven.

A device definition contains raw-control identifiers, raw ranges, match
identifiers, and a mapping to normalized controls. It is serializable and
versioned, and the loader rejects malformed, unknown, oversized, or ambiguous
JSON. No physical device mappings ship with Alpha 0.1; the desktop can save and
recognize the mapping produced by its synthetic teaching fixture.

Personal calibration values are separate from globally reusable hardware definitions.

### 5. Calibration / teaching

The core teaching session accepts timestamped raw samples and supports guided
observation:

- identify requested buttons from changed raw inputs
- identify stick axes from guided movement
- identify trigger axes/ranges
- capture min/max/center/noise where meaningful
- allow skipping controls that are unavailable
- produce a local mapping
- produce a sanitized report suitable for contribution

The desktop currently supplies those samples from a synthetic fixture. It does
not read raw HID reports from a selected physical device. The saved data
distinguishes:
- global device mapping facts
- per-unit calibration facts

### 6. ControllerScript frontend

ControllerScript is a restricted Python-like domain-specific language. Its
implemented v0.1 syntax is documented in CONTROLLERSCRIPT.md: scalar state,
`if`/`else`, non-recursive functions, `press`/`release`/`tap`, `wait`, normalized
input reads, and normalized output writes. It has no imports, loops, collections,
layers, or host-computer capabilities.

Pipeline:

```text
source
  ↓
lexer/parser
  ↓
AST
  ↓
semantic/type validation
  ↓
intermediate representation / bytecode
  ↓
ControllerVM
```

The CLI, desktop editor, future visual editor, future AI configuration, and
source editor must converge on the same validated model rather than maintaining
separate execution engines.

### 7. ControllerVM

The VM owns:

- event dispatch
- timers
- state variables
- deterministic scheduling
- execution quotas
- memory quotas
- output mutation
- error isolation

`wait()` is a yielding scheduler primitive. It does not block input processing.
The desktop advances runtime time on a dispatcher timer; the deterministic
scenario runner advances through timestamped input and drains pending timers.

Untrusted profiles must not have direct:
- filesystem access
- network access
- process access
- registry access
- arbitrary DLL/native-code loading
- kernel access

### 8. Output model

Scripts write to a pending normalized OutputState.

Output changes are committed coherently after each logical input timestamp or
timer deadline so a consumer does not observe intermediate handler writes.

Conflict behavior must be deterministic and documented.

### 9. Output adapters

Required Alpha adapters:

- RecordedOutput for tests
- DebugOutput
- VirtualXboxOutput

VirtualXboxOutput wraps HIDMaestro v1.11.0. The Windows project downloads its
official release archive during build when absent and verifies pinned SHA-256
hashes for the archive and SDK DLL. It does not download at runtime. The core
runtime remains usable with debug preview when the backend is unavailable.

### 10. Simulation and conformance

Physical hardware must not be required to validate most of ControllerOS.

Synthetic fixtures currently model:

- normal gamepad
- noisy stick
- off-center stick
- missing triggers
- an unknown raw device
- malformed device definition

Tests should be able to provide timestamped input events and assert timestamped normalized outputs.

### 11. Hardware report

A contribution report v2 contains only:

- ControllerOS version
- device identifiers needed for matching
- HID/controller capability summary
- mapping observations
- calibration evidence keyed by generated anonymous control IDs
- generated mapping candidate
- validation results

The exporter replaces raw control labels with stable ordinal IDs such as
`button-0` and `axis-0`; this prevents identifier-like input labels from
leaking into a report. It does not intentionally contain:

- username
- computer name
- unrelated USB inventory
- arbitrary file paths
- IP addresses
- account identifiers
- unrelated system telemetry

The exact v2 JSON shape, field types, enum strings, and pre-serialization
validation rules are documented in [HARDWARE_REPORT.md](HARDWARE_REPORT.md).

### 12. Headless control boundary

The long-term application boundary should expose core operations independently
of WPF so automation and remote maintainers can:

- list devices and capabilities
- inspect normalized/raw observations where supported
- compile/validate/simulate profiles
- run/stop profiles
- teach unknown devices
- export sanitized hardware evidence
- query diagnostics

A CLI should be a thin frontend over these same services. It must not become a
parallel implementation of controller behavior.

Manual GUI testing is useful evidence but should not remain a permanent
maintainer bottleneck. Physical-device evidence may come from contributors who
own the hardware.

### 13. AI integration boundary

AI is not part of the Alpha 0.1 completion requirement.

Future AI integrations should operate through structured ControllerOS capabilities:

```text
list devices
read normalized capabilities
read profile schema/language reference
propose ProfilePatch
validate
simulate
explain
apply after user approval
```

An AI provider should not receive unrestricted operating-system access merely because it can edit a controller profile.

## Explicit non-goals

Alpha 0.1 does not require:

- consoles
- Linux/macOS support
- embedded deployment
- profile marketplace
- anti-cheat circumvention
- hardware identity spoofing
- game-specific bypass logic
- process injection
- memory inspection
- packet manipulation
- arbitrary plugin-native-code execution
- proprietary feature support for every controller
- perfect UI polish

## Architectural test

A valid architecture must permit this entire path without physical hardware:

```text
SyntheticInput
    ↓
normalized events
    ↓
ControllerScript
    ↓
ControllerVM
    ↓
RecordedOutput
```

and permit replacing only the boundaries:

```text
RealControllerInput
    ↓
same core
    ↓
VirtualXboxOutput
```
