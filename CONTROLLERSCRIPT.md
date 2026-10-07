# ControllerScript v0.1

## Purpose

ControllerScript is a sandboxed, Python-like language for transforming controller state over time.

It is intentionally not Python. Familiar syntax is desirable, but execution semantics belong to ControllerOS.

The language should be powerful inside this domain:

- buttons
- axes
- timing
- state
- conditions
- functions
- layers
- sequences
- math
- analog transforms

and intentionally unable to escape into unrestricted host-computer control.

## Design rules

1. Deterministic behavior beats Python compatibility.
2. `wait()` always yields; it never blocks the I/O loop.
3. Controller-specific values have bounded types.
4. Programs execute under instruction, memory, timer, and task quotas.
5. Runtime failure disables the offending handler/profile rather than crashing the core service.
6. The compiler rejects behavior it can prove invalid.
7. The text frontend and future visual frontend target the same AST/IR.
8. The v0.1 surface stays small enough to implement and test completely.

## Canonical control names

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

Aliases such as Xbox/PlayStation-specific names may exist in UI presentation, but portable scripts should prefer semantic names.

## Value types

```text
Bool
Int
Float
Duration
Button
Axis
Trigger
LayerName
String          # bounded
List<T>         # bounded
Map<K,V>        # bounded
```

Axis values: `-1.0..1.0`

Trigger values: `0.0..1.0`

Out-of-range controller values must be rejected or explicitly clamped according to the API contract; silent undefined behavior is not allowed.

## Event handlers

Minimum v0.1:

```python
on press(SOUTH):
    tap(EAST)

on release(SOUTH):
    release(EAST)

on change(RIGHT_X):
    output.RIGHT_X = RIGHT_X
```

Future syntax may add hold/double-tap/chords, but Alpha should prefer implementing these as tested library/runtime primitives instead of expanding grammar prematurely.

## Timing

```python
on press(SOUTH):
    press(EAST)
    wait(100ms)
    release(EAST)
```

Semantics:

1. `press(EAST)` mutates pending output.
2. `wait(100ms)` stores continuation state and yields immediately.
3. The scheduler resumes the continuation no earlier than the requested logical time.
4. Input processing continues during the wait.

## Conditions

```python
on press(RIGHT_BUMPER):
    if LEFT_TRIGGER > 0.5:
        tap(WEST)
    else:
        tap(NORTH)
```

## State

```python
state enabled = false

on press(VIEW):
    enabled = not enabled

on press(SOUTH):
    if enabled:
        tap(EAST)
```

State is profile-local unless an explicit future API says otherwise.

## Functions

```python
def precision(x):
    return sign(x) * abs(x) ** 1.5

on change(RIGHT_X):
    output.RIGHT_X = precision(RIGHT_X)
```

Recursion should be disallowed in v0.1 unless and until bounded recursion semantics are specified.

## Layers

Target semantics:

```python
layer "alternate":
    SOUTH -> WEST
    EAST -> NORTH

on press(LEFT_BUMPER):
    activate_layer("alternate")

on release(LEFT_BUMPER):
    deactivate_layer("alternate")
```

Exact grammar may evolve during implementation, but layer activation and output conflict rules must be deterministic.

## Standard library target

Safe modules/functions may include:

```text
math
timing
curves
filters
state
debug
controller
```

There is no general-purpose import mechanism for arbitrary host libraries.

## Forbidden host capabilities for normal profiles

ControllerScript v0.1 must not expose:

```text
filesystem
network sockets
HTTP clients
process launching
process memory
registry
shell
PowerShell
native DLL loading
kernel/driver APIs
arbitrary reflection into host runtime
```

Trusted device/backend development is separate from profile execution.

## Runtime quotas

Exact defaults may be tuned, but the runtime must implement enforceable limits for:

- instructions per uninterrupted handler slice
- concurrent tasks
- active timers
- profile memory
- collection sizes
- string sizes
- nested calls

Quota violation must produce a useful diagnostic and preserve core runtime operation.

## Diagnostics

Source errors should identify line/column and expected syntax.

Semantic errors should identify invalid controls/types/capabilities.

Runtime diagnostics should identify:

- profile
- handler
- event
- quota/error
- whether the handler or profile was disabled

## Bytecode / IR

The IR is an implementation detail in Alpha, but it must be deterministic and independently testable.

Representative operations:

```text
LOAD_CONST
LOAD_STATE
STORE_STATE
READ_CONTROL
WRITE_CONTROL
CALL_BUILTIN
COMPARE
JUMP
JUMP_IF_FALSE
SCHEDULE_CONTINUATION
RETURN
```

Do not prematurely freeze a public bytecode ABI in Alpha 0.1.

## Minimum conformance program

The first complete ControllerScript implementation must successfully compile and execute:

```python
state enabled = true

on press(SOUTH):
    if enabled and LEFT_TRIGGER > 0.5:
        press(EAST)
        wait(100ms)
        release(EAST)
```

The conformance suite must verify output timestamps and prove the wait does not block unrelated input events.
