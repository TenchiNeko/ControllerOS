# ControllerOS Vision

## One sentence

**ControllerOS is a headless-first programmable controller platform that lets people teach the system new hardware, program controller behavior safely, and contribute physical-device knowledge back to the community.**

## Why headless-first

ControllerOS should never depend on its maintainer, contributor, or user sitting at a specific desktop with a specific controller attached.

The core system must be operable through:

- command-line tools
- automation and CI
- structured APIs
- synthetic devices and deterministic fixtures
- remote/headless Windows environments

Graphical interfaces are valuable, but they are frontends to the same core rather than architectural dependencies.

A valid future release should be maintainable from a remote terminal while physical hardware validation can be supplied by people who actually own the devices.

## Product model

```text
                    ControllerOS Core
                           │
          ┌────────────────┼────────────────┐
          │                │                │
          ↓                ↓                ↓
         CLI            Desktop UI       AI/API
          │                │                │
          └────────────────┼────────────────┘
                           ↓
                normalized controller model
                           ↓
                    ControllerScript
                           ↓
                  deterministic runtime
                           ↓
                     output adapters
```

All frontends should converge on the same validated profile/runtime model.

The CLI must not become a second implementation of ControllerOS. The desktop must not become a second implementation either.

## Hardware model

ControllerOS should not require maintainers to buy or physically possess every controller.

The intended hardware loop is:

```text
User plugs in controller
        ↓
ControllerOS recognizes it?
   yes ───────→ load verified definition
   no
        ↓
teach / inspect raw controls
        ↓
generate local mapping + calibration
        ↓
validate locally
        ↓
export privacy-bounded hardware evidence
        ↓
community issue / pull request
        ↓
definition reviewed + tested
        ↓
future users recognize the device automatically
```

Physical hardware owners provide physical evidence.

ControllerOS provides:

- discovery tools
- raw input capture
- guided teaching
- normalization
- validation
- privacy-safe report generation
- reproducible synthetic fixtures
- CI verification

This lets hardware support scale beyond what any one maintainer can own.

## Evidence, not assumptions

Hardware support must remain evidence-based.

A controller can be labeled:

- **maintainer-tested**
- **contributor-tested**
- **mapping-only**
- **unverified**

Software behavior should be testable without hardware wherever possible.

Physical behavior should never be claimed from synthetic evidence alone.

## ControllerScript

ControllerScript is the programmable layer.

The long-term goal is enough expressive power to describe practically any behavior that can be represented as controller state plus time, while preventing ordinary profiles from escaping into unrestricted host-computer control.

That includes domains such as:

- remapping
- accessibility transformations
- stateful controls
- layers
- sequences
- timers
- analog curves
- calibration-aware transformations
- reusable controller-domain libraries

The runtime remains sandboxed and deterministic.

## AI as a frontend, not an authority

AI integration is part of the long-term vision, but AI should configure ControllerOS rather than bypass it.

The intended model is:

```text
User intent
    ↓
AI provider
    ↓
structured profile proposal
    ↓
ControllerOS validator
    ↓
simulation / explanation
    ↓
user approval
    ↓
same ControllerScript/runtime
```

An AI should be replaceable. ControllerOS should be usable without one.

Potential providers may include cloud models, local models, or future systems.

## Community ownership

ControllerOS is intended to be capable of outliving its original author.

The repository should make it possible for contributors to help at multiple levels:

- hardware owners can provide validated device evidence
- non-programmers can submit mappings and reports
- developers can improve runtime, CLI, frontends, and device support
- maintainers can emerge from repeated high-quality contributions

The long-term success condition is not that one person implements every controller.

It is that the platform makes it easy for the community to teach ControllerOS about hardware the original author never owned.

## Maintainer-independent operation

Future work should prefer designs that can be:

- built in CI
- tested headlessly
- exercised through CLI/API
- validated with recorded/synthetic fixtures
- reviewed remotely
- packaged automatically

Manual visual testing should be useful evidence, not a permanent release bottleneck.

Physical hardware testing should be valuable evidence, not a prerequisite for the original maintainer to continue development.

## Boundaries

ControllerOS is a general-purpose controller programming platform.

The project does not need game-specific circumvention features to be powerful.

The platform should not add features whose purpose is:

- anti-cheat circumvention
- detection evasion
- enforcement-bypass hardware identity spoofing
- ban evasion
- process injection
- game-memory manipulation
- packet manipulation

The interesting problem is programmable controller behavior, hardware abstraction, accessibility, automation, and community-supported device knowledge.

## Headless Community Bridge

Alpha 0.1 remains the historical proof of the core runtime, language, synthetic
teaching system, and Windows virtual output path. The Headless Community Bridge
adds a Windows CLI, selected-device raw HID capture, teaching through the
existing calibration engine, privacy-bounded reports, and CI-built Windows
artifacts. Physical controller support remains evidence-based: replay fixtures
validate software behavior, while contributors provide the physical evidence
that maintainers cannot synthesize.

The bridge is the current project focus. It keeps AI, broader language work,
console support, and unrelated frontends outside this phase. Physical-device
reports and interactive visual checks remain useful community evidence, not a
reason for remote maintainers to stop software development.
