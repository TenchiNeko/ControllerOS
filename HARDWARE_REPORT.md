# Hardware report JSON v3

`HardwareReportJson.Serialize` writes a strict, allowlisted JSON document. The
exporter validates before writing, and the CLI validates again before exporting
an existing report. Unknown properties, duplicate properties, oversized input,
invalid mappings, and arbitrary raw control labels are rejected.

## Shape

| Path | Meaning |
| --- | --- |
| `schemaVersion` | Must be `3`. |
| `controllerOSVersion` | Version string, at most 64 characters. |
| `controllerOSCommit` | Full 40–64 character hexadecimal commit, or `unknown` for a local build without commit metadata. |
| `evidenceLevel` | `maintainer-tested`, `contributor-tested`, `mapping-only`, or `unverified`. |
| `device.match` | Nonzero `vendorId`, `productId`, `usagePage`, and `usage`. |
| `device.revision` | Optional 16-bit device revision. |
| `device.connectionMode` | `usb`, `bluetooth`, or `unknown`. |
| `device.retailModelName` | Optional contributor-supplied printable hardware model; path separators, control characters, and IP address literals are rejected. |
| `device.controls[]` | Anonymous raw control ID, kind (`button` or `axis`), and finite HID-declared logical minimum/maximum. |
| `skippedControls[]` | Standard controls marked unavailable during teaching. |
| `mappingEvidence[]` | One raw control, standard target, and calibration record per mapped control. |
| `mappingCandidate` | Generated reusable device definition. Per-unit calibration remains separate. |
| `validation` | Must contain `isValid: true` and an empty `errors` array. |

Each mapping's calibration contains the observed minimum/maximum, resting
center, noise, sample count (1–10000), and mode (`button`, `axis`, or
`trigger`). This separates the HID-declared logical range from the values the
contributor actually observed. Every standard control
must appear exactly once in either `mappingEvidence` or `skippedControls`.
Evidence must agree with the generated mapping and capability range.

Raw control IDs use contiguous anonymous ordinals such as `button-0` and
`axis-0`. The IDs are stable only within one report. They cannot correlate
hardware units or identify a device path. A D-pad hat with an eight-position
range and a null position is expanded into four button observations; other
value arrays and unsupported hat layouts are left unmapped and reported by
`inspect`.

## Privacy boundary

The report includes only version/commit, optional model text, connection mode,
the device match identifiers and revision, anonymous control ranges, generated
mapping, calibration, skipped controls, validation, and evidence level. It has
no fields for usernames, hostnames, device paths, serial numbers, IP addresses,
unrelated inventory, arbitrary file paths, or credentials. The optional model
name is restricted to a short hardware-name character set and cannot be an IP
address literal. Contributors should enter only a known hardware model name.
Keep the raw HID path local; the CLI never prints or serializes it.

## Validation

Serialization and loading reject reports unless all of the following hold:

- Schema, version, commit, evidence level, and connection mode are valid.
- VID/PID/usage-page/usage identifiers are nonzero; optional revision is a
  16-bit value.
- There are 1–128 unique anonymous controls. Each range is finite and
  increasing; ordinal IDs are contiguous by kind.
- The mapping candidate validates as device-definition schema 1 and matches
  the allowlisted identity and generated stable name/ID.
- Each mapped raw control and standard target is unique and its evidence,
  mapping mode, range, and calibration agree.
- Each standard control appears once as a mapping or a skipped control.
- Validation is successful and has no error strings.

The version expression is intentionally narrower than arbitrary text but is
not a complete Semantic Versioning 2.0 validator.
