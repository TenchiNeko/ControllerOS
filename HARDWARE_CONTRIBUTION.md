# Test a controller and share a mapping report

This guide is for controller owners using Windows PowerShell. The CLI works
without a desktop session. It captures only the selected HID collection and
does not need GPU passthrough.

## 1. Download and verify the experimental build

1. Download
   [ControllerOS 0.1.0-alpha.1 for Windows x64](https://github.com/TenchiNeko/ControllerOS/releases/download/v0.1.0-alpha.1/controlleros-0.1.0-alpha.1-win-x64.zip)
   and the adjacent `.zip.sha256` checksum from the public release page.
2. Save both files in **Downloads**. In File Explorer, open Downloads, click
   the address bar, type `powershell`, and press Enter. Compare the checksum
   before extracting:

   ```powershell
   $expected = (Get-Content .\controlleros-0.1.0-alpha.1-win-x64.zip.sha256 -Raw) -split '\s+' | Select-Object -First 1
   $actual = (Get-FileHash .\controlleros-0.1.0-alpha.1-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
   if ($actual -ne $expected) { throw 'Checksum mismatch; download the ZIP again.' } else { 'Checksum verified.' }
   ```

3. Right-click the ZIP, choose **Extract All**, and open the extracted folder.
   In File Explorer, click the address bar, type `powershell`, and press Enter.
   The package includes the CLI and runtime; installing the .NET SDK is not
   required. `BUILD-INFO.txt` records the version and exact source commit.

The folder includes the self-contained CLI, ControllerScript example
profiles, the virtual-output dependency and its license notices, and
`FILE-SHA256SUMS.txt` for the packaged files.

## 2. Check the CLI

Start by checking the executable identity and local installation:

```powershell
.\controlleros.exe --help
.\controlleros.exe version --json
.\controlleros.exe self-test
```

Then connect the controller using the wired, Bluetooth, or dongle connection
you want to test and list available generic HID collections:

```powershell
.\controlleros.exe devices
```

`devices` lists generic HID gamepad and joystick collections. The IDs such as
`controller-001` are temporary indexes for the current list. They do not
identify a controller across runs. Inspect only the controller you plan to
teach:

```powershell
.\controlleros.exe inspect controller-001
```

The CLI prints VID/PID, revision and usage values when available, connection
mode, raw control ranges, and any input value arrays or hat layouts it cannot
map. It never prints the Windows device path.

## 3. Teach the controller

Run `teach` with the ID from the current device list:

```powershell
.\controlleros.exe teach controller-001
```

The CLI asks for an optional retail/model name. Then follow each prompt. Press
Enter to continue, press Enter to start capture, move only the requested
control through its range, let it return to rest, and press Enter to end
capture. Type `skip` when a control is unavailable. If movement is ambiguous,
repeat the capture and move one control at a time. The prompts cover buttons,
sticks, triggers, and supported D-pad controls; they do not imply support for
proprietary functions.

At the end, review the generated mapping and validation preview. Type `yes` at
the physical-test prompt only if you personally tested this physical
controller. Otherwise the report is marked `unverified`. Type `yes` at the
save prompt to write the local definition, separate per-unit calibration, and
report; any other answer leaves the preview unsaved.

The CLI prints the local paths and calibration ID. Each teaching run creates a
new calibration ID. Its local calibration file is bound to both the generated
definition and a hash of the selected HID interface path, so runtime rejects a
different unit's calibration. This local hash and the HID path never enter the
community report. Local files are stored under `%LOCALAPPDATA%\ControllerOS`.

## 4. Inspect and validate the report

The latest sanitized report is stored at
`%LOCALAPPDATA%\ControllerOS\reports\latest.json`. Export a copy to the
current folder and validate it:

```powershell
.\controlleros.exe export-report "$env:LOCALAPPDATA\ControllerOS\reports\latest.json" .\hardware-report.json
.\controlleros.exe validate .\hardware-report.json
Get-Content .\hardware-report.json
```

Review the file before sending or attaching it. The exporter includes only ControllerOS
version/commit, optional retail/model name, connection mode, VID/PID/revision/
usage values, anonymous raw controls and observed ranges, mappings, calibration,
skipped controls, validation, and evidence level. It excludes usernames,
hostnames, device paths, serial numbers, IP addresses, unrelated device
inventory, arbitrary file paths, and credentials.

## 5. Submit through GitHub

Open **Issues → New issue → Controller Compatibility Report** in the
repository. Fill in the connection mode, Windows version, and controls you
tested or skipped. Attach the reviewed JSON report if useful. Describe any
behavior generic HID did not expose. Do not attach full system logs or device
inventory. The template does not ask for usernames, machine names, or account
details.

## Troubleshooting

- **No controller detected:** run `devices` again after connecting the
  controller. Try another USB port or reconnect it, then run `self-test` to
  check Windows HID enumeration. The command lists generic HID gamepad and
  joystick collections only.
- **Unsupported HID interface:** use `inspect <controller-id>` to see which
  scalar controls are understood. Unsupported value arrays or hat layouts are
  left unmapped; report the limitation instead of guessing a mapping.
- **Wired works but Bluetooth does not (or the reverse):** test each mode
  separately. Windows may expose different HID collections or controls for
  each connection mode; create and report a separate observation for each.
- **Virtual controller dependency is missing:** teaching, validation,
  simulation, and self-test do not activate virtual output. `runtime start`
  needs the bundled HIDMaestro dependency and may ask for elevation to install
  its signed driver. Do not disable Windows security features.
- **Windows permissions or blocked driver:** standard discovery and teaching
  should not need administrator rights. Only first activation of virtual
  output may need elevation. Follow the Windows driver prompt and stop if the
  publisher or signature is unexpected; do not turn off security protections.
- **Unexpected disconnection:** reconnect the selected controller, run
  `devices` again, and use the new temporary ID. Repeat the incomplete control
  capture; saved calibration is tied to that physical HID interface.
- **Proprietary buttons or features:** paddles, gyro, adaptive triggers,
  touchpads, LEDs, speakers, and similar features are unsupported unless the
  interface exposes data that ControllerOS understands. Mark them unavailable
  in the report.
- **ControllerScript compilation fails:** run
  `controlleros.exe validate .\examples\starter-profile.json` to check the
  included example, then compare your profile to
  [CONTROLLERSCRIPT.md](CONTROLLERSCRIPT.md). The CLI reports the failing
  profile or source location; loops, imports, and host-computer APIs are not
  part of ControllerScript v0.1.

## Evidence labels

- **maintainer-tested** — a maintainer physically validated the controller.
- **contributor-tested** — the report author physically tested the controller.
- **mapping-only** — a mapping exists, but physical validation is absent.
- **unverified** — the mapping or hardware identity remains experimental.

Synthetic fixtures and virtual devices are useful software checks; they do not
establish physical-controller support. Generic HID cannot independently expose
every proprietary paddle, gyro, adaptive trigger, touchpad, LED, speaker, or
other special feature. Reports should describe those controls as unavailable
unless the captured evidence and current mapping model understand them.
