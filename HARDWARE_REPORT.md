# Hardware report JSON v2

`HardwareReportJson.Serialize` emits indented JSON with camelCase property
names. Enum strings use `JsonNamingPolicy.CamelCase`; enum names containing
underscores therefore retain the casing produced by that policy, for example
`LEFT_STICK_X` serializes as `lefT_STICK_X`.
Report validation runs before serialization, and integer enum values are not
emitted.

## Shape

| Path | JSON type | Meaning |
| --- | --- | --- |
| `schemaVersion` | integer | Report schema; must be `2`. |
| `controllerOSVersion` | string | ControllerOS version string accepted by the current validator. |
| `device.match` | object | `vendorId`, `productId`, `usagePage`, and `usage`, each a nonzero 16-bit identifier. |
| `device.controls[]` | object | `id`, `kind` (`button` or `axis`), `minimum`, and `maximum`. |
| `mappingEvidence[]` | object | `rawControlId`, standard `target`, and `calibration`. |
| `mappingEvidence[].calibration` | object | `minimum`, `maximum`, `center`, `noise`, `sampleCount` (1–10000), and `mode` (`button`, `axis`, or `trigger`). |
| `mappingCandidate` | object | A device definition: `schemaVersion`, `id`, `displayName`, `match`, and `mappings[]`. |
| `mappingCandidate.mappings[]` | object | `rawControlId`, standard `target`, `rawKind`, `mode`, `rawMinimum`, and `rawMaximum`. |
| `validation` | object | `isValid` and an `errors` string array. Exported reports must have `isValid: true` and no errors. |

Enum values are strings transformed by `JsonNamingPolicy.CamelCase`, for
example `south`, `lefT_TRIGGER`, `lefT_STICK_X`, and `button`. Standard
control IDs are listed in
[CONTROLLERSCRIPT.md](CONTROLLERSCRIPT.md#controls-and-values).

## Example

This excerpt shows the exact nested JSON property names and enum encoding. A
complete report has one control summary for every discovered raw control,
evidence for every mapping, and a matching mapping candidate.

```json
{
  "schemaVersion": 2,
  "controllerOSVersion": "0.1.0-alpha.1",
  "device": {
    "match": {
      "vendorId": 4660,
      "productId": 22136,
      "usagePage": 1,
      "usage": 5
    },
    "controls": [
      { "id": "axis-0", "kind": "axis", "minimum": -1000, "maximum": 1000 },
      { "id": "button-0", "kind": "button", "minimum": 0, "maximum": 1 }
    ]
  },
  "mappingEvidence": [
    {
      "rawControlId": "button-0",
      "target": "south",
      "calibration": {
        "minimum": 0,
        "maximum": 1,
        "center": 0,
        "noise": 0,
        "sampleCount": 3,
        "mode": "button"
      }
    }
  ],
  "mappingCandidate": {
    "schemaVersion": 1,
    "id": "hid-1234-5678-0001-0005",
    "displayName": "Unknown controller 1234:5678",
    "match": {
      "vendorId": 4660,
      "productId": 22136,
      "usagePage": 1,
      "usage": 5
    },
    "mappings": [
      {
        "rawControlId": "button-0",
        "target": "south",
        "rawKind": "button",
        "mode": "button",
        "rawMinimum": 0,
        "rawMaximum": 1
      }
    ]
  },
  "validation": {
    "isValid": true,
    "errors": []
  }
}
```

The serializer checks that device control IDs use the anonymous
`button-0`, `axis-0` form, have no gaps within each kind, and agree with the
mapping candidate and evidence. It rejects arbitrary HID labels even when a
caller constructs a report directly instead of using the report generator.
The IDs are stable ordinals within one report, not identifiers that can be
used to correlate devices across reports.

## Validation rules

Serialization is rejected unless all of these conditions hold:

- `schemaVersion` is `2`. `controllerOSVersion` is at most 64 characters and
  matches `\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\z`.
- The four device match identifiers are nonzero. There are 1–128 control
  summaries. Each minimum and maximum is finite, increasing, and has a finite
  difference. IDs use the ordinal form described above, agree with the
  `button`/`axis` kind, and are contiguous from zero within each kind.
- `mappingCandidate` validates as device-definition schema version 1. Its
  match rule equals `device.match`; its ID is the lowercase hexadecimal form
  `hid-{vendorId:x4}-{productId:x4}-{usagePage:x4}-{usage:x4}`, and its name
  is `Unknown controller {vendorId:x4}:{productId:x4}`.
- The candidate has 1–21 mappings, with unique raw IDs and standard targets.
  Mapping ranges are finite, increasing, and have finite width. Button targets
  require button kind/mode; axis targets require axis kind/mode; trigger
  targets require axis kind and trigger mode.
- There are at most 21 evidence entries, with one entry for each candidate
  mapping and no repeated target. Evidence IDs and targets match both the
  candidate and a device control. Candidate raw kind/range must equal that
  control's kind/range.
- Calibration minimum, maximum, center, noise, and width (`maximum - minimum`)
  are finite; minimum is less than maximum; center is within the range; noise
  is nonnegative; and sample count is 1–10000. Calibration range must stay
  inside the raw capability and calibration mode must equal its mapping mode.
  Button calibration requires
  center equal to minimum and zero noise. Axis calibration requires center
  strictly between its extrema and noise smaller than both side ranges.
  Trigger calibration requires noise smaller than the full range.
- `validation.isValid` is true and `validation.errors` is empty.

The version expression above is the implementation's accepted syntax; it is
not a complete Semantic Versioning 2.0 validator.
