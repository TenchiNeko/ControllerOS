# Send a controller mapping report

This guide is for controller owners using Windows PowerShell. The CLI works
without a desktop session. It captures only the selected HID collection and
does not need GPU passthrough.

## 1. Download the experimental Windows build

1. Sign in to GitHub and open the ControllerOS repository's **Actions** tab.
2. Open the latest successful **CI** run for `main` and download
   `controlleros-experimental-win-x64-<commit>` from its Artifacts section.
   For a build you are reviewing in a pull request, use that PR's CI run.
3. Extract the downloaded ZIP into a folder. Open `BUILD-INFO.txt` and confirm
   the commit. The artifact is experimental and expires after 30 days; it is
   not a GitHub Release.

The folder includes the self-contained CLI, the virtual-output dependency and
its license notices, and a `SHA256SUMS.txt` file.

## 2. Check the CLI

Connect the controller directly or through the connection mode you want to
report, then open PowerShell in the extracted folder:

```powershell
.\controlleros.exe self-test
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

The CLI asks for an optional retail/model name. Then follow each prompt. For a
control, press Enter to continue, press Enter again to start capture, move only
that control through its range, let it return to rest, and press Enter to end
capture. Type `skip` when a control is unavailable. If movement is ambiguous,
repeat the capture and move one control at a time.

At the end, review the generated mapping and validation preview. Confirm that
you personally tested a physical controller only if that is true. Type `yes`
to save the local definition, separate per-unit calibration, and report. A
second `yes` is required to save; any other answer leaves the preview unsaved.

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

Review the file before sending it. The exporter includes only ControllerOS
version/commit, optional retail/model name, connection mode, VID/PID/revision/
usage values, anonymous raw controls and observed ranges, mappings, calibration,
skipped controls, validation, and evidence level. It excludes usernames,
hostnames, device paths, serial numbers, IP addresses, unrelated device
inventory, arbitrary file paths, and credentials.

## 5. Submit through GitHub

Open **Issues → New issue → Hardware support report** in the repository. Fill
in the connection mode and the controls you tested or skipped. Paste the JSON
report into the issue body as a fenced `json` block. Describe any behavior the
generic HID report did not expose. Do not attach full system logs or inventory.

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
