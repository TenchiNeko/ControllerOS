# ControllerOS

**A programmable controller runtime with self-learning hardware support.**

ControllerOS is an open-source experiment in treating game controllers as programmable devices rather than fixed button maps.

The project is built around four ideas:

1. **Universal controller model** — normalize buttons, sticks, triggers, hats, and other controls into a hardware-independent representation.
2. **ControllerScript** — a sandboxed, Python-like language for controller behavior: remapping, timing, layers, state, analog transforms, sequences, and accessibility workflows.
3. **Teach unknown hardware** — if a controller is not recognized, guide the user through a calibration/mapping flow and produce a reusable device definition.
4. **One runtime, multiple interfaces** — text code, a future visual editor, and future AI-assisted configuration should all produce the same validated internal profile representation.

## Why this exists

Most controller tools are either device-specific, configuration-heavy, or centered on fixed remapping and macros. ControllerOS explores a different abstraction:

```text
Physical controller
        ↓
device discovery / mapping
        ↓
normalized ControllerState
        ↓
ControllerScript runtime
        ↓
validated output state
        ↓
virtual controller
        ↓
game / application
```

The long-term goal is that a user should be able to plug in a supported controller and program its behavior without needing to understand the controller's raw HID layout.

If the device is unknown, ControllerOS should help the user teach the system how it behaves.

## Alpha 0.1 target

The first public alpha is intentionally narrow:

- Windows-first
- synthetic controller source for fully automated development
- keyboard-backed test input
- normalized controller state
- small deterministic ControllerScript runtime
- asynchronous timers / waits
- virtual Xbox-style output
- unknown-device discovery
- guided standard-control calibration
- portable device-definition files
- hardware-report export for community contributions
- automated conformance tests
- documented contribution path

Alpha 0.1 is **not** intended to support every proprietary controller feature, consoles, anti-cheat circumvention, or every advanced automation feature.

## ControllerScript concept

```python
on press(SOUTH):
    wait(100ms)
    tap(EAST)

on axis(RIGHT_X):
    output.RIGHT_X = curve(RIGHT_X, 1.35)

on hold(LEFT_BUMPER):
    use_layer("precision")
```

ControllerScript is intended to feel familiar while remaining restricted to the controller domain. Community profiles should not receive arbitrary filesystem, process, network, registry, or native-code access.

## Community hardware model

ControllerOS will not require maintainers to physically own every controller.

For recognized devices, a stored mapping is loaded.

For unknown devices, the user should be able to run a guided discovery flow:

```text
Unknown controller detected
        ↓
Press SOUTH
Press EAST
Move left stick
Move right stick
Press triggers
Identify remaining controls
        ↓
Generate device definition
        ↓
Validate locally
        ↓
Optional sanitized hardware report
        ↓
Community contribution
```

Hardware definitions should be data-driven whenever possible so adding a conventional controller can be a small mapping contribution instead of an application-code change.

## Project principles

- Keep the runtime deterministic and testable.
- Treat physical hardware as an adapter around a hardware-independent core.
- Prefer data-driven device support over hard-coded device logic.
- Make unsafe or pathological profiles fail locally without taking down the controller service.
- Keep ControllerScript powerful inside the controller domain and intentionally weak outside it.
- Preserve a recovery path if a profile blocks or suppresses normal controls.
- Do not build game-specific detection evasion, anti-cheat bypasses, hardware-identity spoofing, or ban-evasion features.
- Do not add features merely because they are possible; implement the smallest coherent platform first.

## Repository status

**Pre-alpha / architecture bootstrap.**

See:

- [ARCHITECTURE.md](ARCHITECTURE.md)
- [CONTROLLERSCRIPT.md](CONTROLLERSCRIPT.md)
- [ROADMAP.md](ROADMAP.md)
- [ACCEPTANCE.md](ACCEPTANCE.md)
- [CONTRIBUTING.md](CONTRIBUTING.md)
- [AGENTS.md](AGENTS.md)
- [PROJECT_STATUS.md](PROJECT_STATUS.md)
- [CODEX_START.md](CODEX_START.md)

## License

ControllerOS is licensed under the [Apache License 2.0](LICENSE).

## Contribution philosophy

A contributor should be able to help even if they are not a systems programmer.

Useful contributions can include:

- testing a controller model
- producing a sanitized hardware report
- correcting a device mapping
- adding conformance fixtures
- improving documentation
- implementing a well-scoped runtime milestone
- reviewing ControllerScript behavior

The project should be able to outgrow its original author without losing its architectural constraints.
