---
name: Hardware support report
about: Submit a sanitized ControllerOS hardware mapping report
title: "[hardware] "
---

## Controller model and connection

Retail/model name:

Connection mode (wired, Bluetooth, vendor dongle, or other):

## What you tested

Controls tested and observed:

Controls skipped or still unverified:

Evidence level (maintainer-tested, contributor-tested, mapping-only, or unverified):

## Device data and evidence

Use the experimental headless CLI to produce a validated hardware report. A
`mapping-only` or `unverified` report must not be described as a physical test.
Include the report's ControllerOS version/commit and evidence level. The report
already contains only stable VID/PID/revision/usage values, anonymous raw
control identifiers/ranges, mappings, calibration, skipped controls, and
validation results. Review it before sharing.

Do not paste device paths, serial numbers, usernames, computer names, IP
addresses, account identifiers, a full USB inventory, or unrelated logs.

## Additional notes

Share only details needed to reproduce the mapping behavior.
