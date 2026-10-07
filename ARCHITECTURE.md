# ControllerOS Architecture

## Objective

ControllerOS is a Windows-first programmable controller runtime. The architecture must keep controller-independent logic isolated from physical hardware and virtual-output implementations.

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

## Alpha implementation choice

For Alpha 0.1, prefer C# on a current supported .NET runtime to minimize Windows integration friction and maximize iteration speed.

Do not make the public profile format, ControllerScript syntax, bytecode, hardware-definition schema, or conformance fixtures depend on CLR-specific concepts. They are platform contracts, not implementation details.

## Components

### 1. Device discovery

Responsibilities:

- enumerate controller-like/HID devices available to the Windows guest
- identify devices using stable descriptors where available
- expose vendor/product/revision/interface metadata
- determine whether a stored device definition exists
- route unknown devices into discovery/calibration

No game-specific process inspection belongs here.

### 2. Input adapters

Every input backend converts raw device behavior into the same normalized model.

Required Alpha adapters:

- SyntheticInput
- KeyboardTestInput
- WindowsControllerInput

Physical adapters must not contain ControllerScript behavior.

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

A device definition contains enough information to translate observable raw controls into normalized controls. It must be serializable, versioned, schema-validated, and testable without having the hardware connected.

Personal calibration values are separate from globally reusable hardware definitions.

### 5. Calibration / teaching

Unknown devices should be teachable through guided observation:

- identify requested buttons from changed raw inputs
- identify stick axes from guided movement
- identify trigger axes/ranges
- capture min/max/center/noise where meaningful
- allow skipping controls that are unavailable
- produce a local mapping
- produce a sanitized report suitable for contribution

The wizard must distinguish:
- global device mapping facts
- per-unit calibration facts

### 6. ControllerScript frontend

ControllerScript is a Python-like domain-specific language.

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

The visual editor, future AI configuration, and source editor must converge on the same validated model rather than maintaining separate execution engines.

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

`wait()` is a yielding scheduler primitive. It must never block the controller I/O loop.

Untrusted profiles must not have direct:
- filesystem access
- network access
- process access
- registry access
- arbitrary DLL/native-code loading
- kernel access

### 8. Output model

Scripts write to a pending normalized OutputState.

Output changes are committed coherently on an output tick so a consumer does not observe arbitrary intermediate states.

Conflict behavior must be deterministic and documented.

### 9. Output adapters

Required Alpha adapters:

- RecordedOutput for tests
- DebugOutput
- VirtualXboxOutput

The core runtime must remain usable even when a virtual-controller backend is unavailable.

### 10. Simulation and conformance

Physical hardware must not be required to validate most of ControllerOS.

Synthetic fixtures should model:

- normal gamepad
- noisy stick
- off-center stick
- missing triggers
- disconnect/reconnect
- unknown device
- malformed device definition

Tests should be able to provide timestamped input events and assert timestamped normalized outputs.

### 11. Hardware report

A contribution report may contain:

- ControllerOS version
- device identifiers needed for matching
- HID/controller capability summary
- mapping observations
- anonymous calibration samples useful for diagnosis
- generated mapping candidate
- validation results

It must not intentionally contain:

- username
- computer name
- unrelated USB inventory
- arbitrary file paths
- IP addresses
- account identifiers
- unrelated system telemetry

### 12. AI integration boundary

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
