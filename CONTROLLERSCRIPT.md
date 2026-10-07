# ControllerScript v0.1

ControllerScript is a small, sandboxed language for responding to normalized
controller input. It is not Python. The compiler produces ControllerOS bytecode
and the runtime exposes no filesystem, network, process, registry, native-code,
or operating-system APIs to a profile.

## Controls and values

The standard controls are:

```text
SOUTH EAST WEST NORTH
LEFT_BUMPER RIGHT_BUMPER
LEFT_TRIGGER RIGHT_TRIGGER
LEFT_STICK_X LEFT_STICK_Y RIGHT_STICK_X RIGHT_STICK_Y
LEFT_STICK_CLICK RIGHT_STICK_CLICK
DPAD_UP DPAD_DOWN DPAD_LEFT DPAD_RIGHT
MENU VIEW GUIDE
```

Control names are case-insensitive. `LEFT_X`, `LEFT_Y`, `RIGHT_X`, and
`RIGHT_Y` are aliases for the corresponding stick axes. Buttons read as
Booleans; axes and triggers read as numbers. Axes are in `-1.0..1.0`, triggers
in `0.0..1.0`.

State variables are declared with a literal initial value:

```python
state enabled = false
state threshold = 0.5
state delay = 100ms
state mode = "alternate"
```

State values may be Boolean, finite number, duration, or string. Strings are
limited to 256 characters and support assignment and equality comparison; there
are no string operations. State is private to one profile. Assignment must keep
the declared value type. Button outputs require Booleans; axis and trigger
outputs require finite numbers in their normalized range. An invalid runtime
value disables the offending handler and reports a diagnostic.

## Source structure and statements

Top-level declarations are `state`, `def`, and `on`. Blocks use four spaces;
tabs are not allowed. `#` begins a comment. Keywords are lowercase. The
supported statements are state assignment, `output.CONTROL = expression`,
`if`/`else`, function calls, and `return` inside functions.

```python
state enabled = true

def scaled(x):
    return sign(x) * abs(x) ** 1.5

on change(RIGHT_X):
    if enabled:
        output.RIGHT_X = scaled(RIGHT_X)
    else:
        output.RIGHT_X = 0
```

Functions accept up to 16 numeric parameters, may return a number or no value,
and may call other functions. Recursive and mutually recursive functions are
rejected. Functions can use the same state, output, button, and wait statements
as handlers.

Expressions support Boolean literals; finite decimal numbers; quoted strings;
duration literals (`ms` or `s`); parentheses; unary `+`, `-`, and `not`; `+`,
`-`, `*`, `/`, and right-associative `**`; comparisons; equality; and Boolean
`and`/`or`. These operators short-circuit: `and` skips its right side when the
left side is false, and `or` skips its right side when the left side is true.
Division by zero and non-finite math results are runtime errors. The math functions are `abs(x)`,
`sign(x)`, `min(x, y)`, and `max(x, y)`.

## Events and timing

```python
on press(SOUTH):
    press(EAST)
    wait(100ms)
    release(EAST)

on release(SOUTH):
    tap(WEST)

on change(RIGHT_X):
    output.RIGHT_X = RIGHT_X
```

`press` and `release` handlers accept buttons. `change` handles axis and trigger
value changes. `press(CONTROL)`, `release(CONTROL)`, and `tap(CONTROL)` write a
button output; `tap` releases after 30 ms unless a later write to that output
supersedes its release. `wait(duration)` yields a handler continuation and does
not block input processing. Durations must be positive and no longer than one
hour. A profile may declare a duration in state and pass it to `wait`.

All input events with one logical timestamp must be submitted as one batch. The
runtime processes input events in sequence order, runs their handlers before
timers due at that timestamp, then commits one coherent output state. Timers due
at the same time run in creation order. Timers due before a later input timestamp
run and commit at their due times. The desktop advances idle runtime time on its
dispatcher timer, so waits complete without another controller event. Replay
uses the same scheduler and drains remaining timers at scenario completion.

## Runtime limits

Defaults are enforced by the compiler, parser, scheduler, and VM:

| Resource | Limit |
| --- | ---: |
| Source | 65,536 characters |
| State variables | 64 |
| Functions | 32 |
| Event handlers | 128 |
| Compiled instructions | 10,000 |
| Instructions per uninterrupted task slice | 10,000 |
| Nested function calls | 16 |
| State string length | 256 characters |
| Concurrent tasks | 64 |
| Active timers | 128 |
| Nested blocks / expression nesting | 64 / 128 |

The parser also limits string literals to 256 characters. Quotas can be
configured by the host for tests. A task failure or quota violation disables
the offending handler, cancels its continuations, resets pending output, and
reports a source-aware runtime diagnostic. A failing output adapter stops the
profile and receives a best-effort neutral reset.

## Deliberate v0.1 exclusions

There are no imports, loops, collections, layers, hold/chord/double-tap events,
axis-specific event syntax, arbitrary string operations, or host-computer
capabilities. Axis and trigger behavior is expressed with `change` handlers and
the supported expressions. These features are outside this implemented
language version.

## Conformance example

The core test suite compiles and executes the following program with synthetic
input, checking the delayed output trace and that unrelated events continue
during the wait:

```python
state enabled = true

on press(SOUTH):
    if enabled and LEFT_TRIGGER > 0.5:
        press(EAST)
        wait(100ms)
        release(EAST)
```
