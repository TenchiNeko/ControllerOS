# Build and runtime dependencies

ControllerOS core and CLI code use the .NET 10 SDK and framework. The Windows
CLI artifact is published self-contained for `win-x64`, so a contributor does
not need to install .NET before running it.

## Windows virtual output dependency

The existing virtual Xbox output adapter uses HIDMaestro v1.11.0 from its
upstream GitHub release. `src/ControllerOS.Windows/ControllerOS.Windows.csproj`
pins the release archive SHA-256 to
`51C5A957FEB7573CF60EE8F30F791DD013053208EAF23D013E28B438303FBDF2` and the
`HIDMaestro.Core.dll` SHA-256 to
`36CAEFc2F457A7C69ECA10E44D9E9750A3991F256EAFEa6000DCA0E450E0635C`.
The dependency is MIT licensed. Its full `LICENSE` and
`THIRD-PARTY-NOTICES.txt` files are copied into Windows build and publish
outputs. The latter records the bundled `usbip-win2` BSD 2-Clause notices and
the upstream installer hashes.

The CLI does not install or start the virtual output driver during discovery,
teaching, simulation, or self-test. `runtime start` activates it; first use may
install its signed driver package and requires an elevated Windows process.

## GitHub Actions

The CI workflow uses `actions/checkout@v4`, `actions/setup-dotnet@v4`, and
`actions/upload-artifact@v4`. These actions are MIT licensed. Workflow token
permissions remain `contents: read`; artifact upload uses the Actions artifact
service and does not need repository contents write access. The published
artifact includes a SHA-256 manifest and the exact source commit in
`BUILD-INFO.txt`.
