using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ControllerOS.Core.ControllerScript;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Devices;
using ControllerOS.Core.Input;
using ControllerOS.Core.Output;
using ControllerOS.Core.Profiles;
using ControllerOS.Core.Reports;
using ControllerOS.Core.Simulation;
using ControllerOS.Windows;

namespace ControllerOS.Cli;

public static class Program
{
    public static Task<int> Main(string[] args) => ControllerOsCli.RunAsync(args, Console.In, Console.Out, Console.Error);
}

public static class ControllerOsCli
{
    private const int SuccessCode = 0;
    private const int ErrorCode = 1;
    private const int UsageCode = 2;
    private const int InvalidInputCode = 3;
    private const int PlatformCode = 4;
    private const int CancelledCode = 130;
    private const string RuntimeStopPipe = "ControllerOS-Runtime-Stop";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        IWindowsDeviceEnumerator? deviceEnumerator = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        bool jsonRequested = args.Contains("--json", StringComparer.Ordinal);
        ParsedCommand command;
        try
        {
            command = CommandLineParser.Parse(args);
        }
        catch (CommandLineParseException exception)
        {
            await WriteFailureAsync("usage", UsageCode, exception.Message, jsonRequested, output, error).ConfigureAwait(false);
            return UsageCode;
        }

        try
        {
            CliResult result = command.Name switch
            {
                "help" => HelpResult(),
                "devices" => Devices(deviceEnumerator ?? new WindowsHidDeviceEnumerator()),
                "inspect" => Inspect(deviceEnumerator ?? new WindowsHidDeviceEnumerator(), command.Arguments[0]),
                "teach" => await TeachAsync(deviceEnumerator ?? new WindowsHidDeviceEnumerator(), command.Arguments, input, output).ConfigureAwait(false),
                "validate" => Validate(command.Arguments[0]),
                "simulate" => Simulate(command.Arguments[0]),
                "export-report" => ExportReport(command.Arguments, command.Json),
                "self-test" => SelfTest(deviceEnumerator ?? new WindowsHidDeviceEnumerator()),
                "runtime start" => await StartRuntimeAsync(deviceEnumerator ?? new WindowsHidDeviceEnumerator(), command.Arguments, output).ConfigureAwait(false),
                "runtime stop" => await StopRuntimeAsync().ConfigureAwait(false),
                _ => throw new CommandLineParseException("Use controlleros --help to list commands.")
            };
            if (command.Json)
                await output.WriteLineAsync(JsonSerializer.Serialize(new { success = result.ExitCode == SuccessCode, exitCode = result.ExitCode, command = command.Name, message = result.Message, data = result.Data }, JsonOptions)).ConfigureAwait(false);
            else if (result.WriteDataDirectly)
                await output.WriteLineAsync((string)result.Data!).ConfigureAwait(false);
            else if (!string.IsNullOrEmpty(result.Message))
                await output.WriteLineAsync(result.Message).ConfigureAwait(false);
            return result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await WriteFailureAsync(command.Name, CancelledCode, "Operation cancelled.", command.Json, output, error).ConfigureAwait(false);
            return CancelledCode;
        }
        catch (PlatformNotSupportedException exception)
        {
            await WriteFailureAsync(command.Name, PlatformCode, exception.Message, command.Json, output, error).ConfigureAwait(false);
            return PlatformCode;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or JsonException or
            ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or
            System.ComponentModel.Win32Exception or DeviceDefinitionFormatException or ControllerProfileFormatException)
        {
            int exitCode = exception is FileNotFoundException or DirectoryNotFoundException or JsonException or ArgumentException or
                DeviceDefinitionFormatException or ControllerProfileFormatException ? InvalidInputCode : ErrorCode;
            await WriteFailureAsync(command.Name, exitCode, exception.Message, command.Json, output, error).ConfigureAwait(false);
            return exitCode;
        }
    }

    private static CliResult HelpResult() => new(
        "ControllerOS is a headless Windows controller teaching and runtime CLI.\n" +
        "Commands: devices, inspect, teach, validate, simulate, export-report, runtime start, runtime stop, self-test.\n" +
        "Use --json with non-interactive commands. Controller IDs are temporary one-based indexes from devices.",
        new
        {
            commands = new[]
            {
                "controlleros devices [--json]",
                "controlleros inspect <controller-id> [--json]",
                "controlleros teach <controller-id> [--model <name>]",
                "controlleros validate <profile-or-report.json> [--json]",
                "controlleros simulate <profile.json> [--json]",
                "controlleros export-report [input-report.json] [output-report.json] [--json]",
                "controlleros runtime start <profile.json> --device <controller-id> --definition <definition-id> --calibration <calibration-id>",
                "controlleros runtime stop [--json]",
                "controlleros self-test [--json]"
            }
        });

    private static CliResult Devices(IWindowsDeviceEnumerator enumerator)
    {
        IReadOnlyList<WindowsHidDevice> devices = GetControllers(enumerator);
        DeviceListItem[] items = devices.Select((device, index) => new DeviceListItem(
            ControllerId(index), DisplayName(device.Product), device.VendorId!.Value, device.ProductId!.Value,
            device.Revision, device.ConnectionMode, device.UsagePage!.Value, device.Usage!.Value,
            device.InputReportBytes, device.InputButtonCapabilityCount, device.InputValueCapabilityCount)).ToArray();
        string message = items.Length == 0
            ? "No generic HID gamepad or joystick collections are currently available."
            : string.Join(Environment.NewLine, items.Select(item =>
                $"{item.Id}  {item.Name}  VID:PID {item.VendorId:X4}:{item.ProductId:X4}  {item.ConnectionMode}"));
        return new(message, items);
    }

    private static CliResult Inspect(IWindowsDeviceEnumerator enumerator, string controllerId)
    {
        WindowsHidDevice device = SelectController(enumerator, controllerId);
        using WindowsHidDeviceEnumerator.WindowsHidInputCapture capture = ((WindowsHidDeviceEnumerator)enumerator).OpenInputCapture(device);
        RawDeviceDescriptor descriptor = capture.Descriptor;
        string controls = string.Join(Environment.NewLine, descriptor.Controls.Select(control =>
            $"  {control.Id,-12} {control.Kind,-6} {control.Minimum:0.###} .. {control.Maximum:0.###}"));
        string message = $"{controllerId}: VID:PID {descriptor.Match.VendorId:X4}:{descriptor.Match.ProductId:X4}, " +
            $"revision {FormatHex(descriptor.Revision)}, usage {descriptor.Match.UsagePage:X4}:{descriptor.Match.Usage:X4}, " +
            $"connection {descriptor.ConnectionMode}; {descriptor.Controls.Count} scalar controls." +
            (capture.IgnoredValueArrayCount > 0 ? $" {capture.IgnoredValueArrayCount} value arrays or unsupported hats are not mapped." : string.Empty) +
            Environment.NewLine + controls;
        return new(message, new
        {
            id = controllerId,
            descriptor.Match.VendorId,
            descriptor.Match.ProductId,
            revision = descriptor.Revision,
            usagePage = descriptor.Match.UsagePage,
            usage = descriptor.Match.Usage,
            descriptor.ConnectionMode,
            controls = descriptor.Controls,
            unsupportedValueCapabilities = capture.IgnoredValueArrayCount
        });
    }

    private static async Task<CliResult> TeachAsync(IWindowsDeviceEnumerator enumerator, IReadOnlyList<string> arguments, TextReader input, TextWriter output)
    {
        WindowsHidDevice device = SelectController(enumerator, arguments[0]);
        string? model = arguments.Count == 3 ? arguments[2] : null;
        if (arguments.Count == 1)
        {
            await output.WriteLineAsync("Retail/model name (optional; press Enter to omit):").ConfigureAwait(false);
            model = await input.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(model))
                model = null;
        }

        using WindowsHidDeviceEnumerator.WindowsHidInputCapture capture = ((WindowsHidDeviceEnumerator)enumerator).OpenInputCapture(device, model);
        var session = new DeviceTeachingSession(capture.Descriptor);
        foreach (ControlId control in ControlCatalog.All)
        {
            while (true)
            {
                await output.WriteLineAsync($"{TeachingPrompt(control)} Type 'skip' if unavailable, or press Enter to continue.").ConfigureAwait(false);
                string? response = await input.ReadLineAsync().ConfigureAwait(false);
                if (string.Equals(response?.Trim(), "skip", StringComparison.OrdinalIgnoreCase))
                {
                    session.Skip(control);
                    break;
                }

                await output.WriteLineAsync("Press Enter to begin capture. Move one control through its range, let it return to rest, then press Enter to stop capture.").ConfigureAwait(false);
                _ = await input.ReadLineAsync().ConfigureAwait(false);
                try
                {
                    IReadOnlyList<RawInputSample> samples = await CaptureStepAsync(capture, input, output).ConfigureAwait(false);
                    session.Observe(control, samples);
                    DeviceControlMapping mapping = session.GetIdentifiedMapping(control)!;
                    await output.WriteLineAsync($"Detected {mapping.RawControlId} for {control}.").ConfigureAwait(false);
                    break;
                }
                catch (InvalidOperationException exception)
                {
                    await output.WriteLineAsync($"Could not identify {control}: {exception.Message} Repeat the capture, or type 'skip' at the next prompt.").ConfigureAwait(false);
                }
                catch (ArgumentException exception)
                {
                    await output.WriteLineAsync($"Capture for {control} was outside a valid calibration range: {exception.Message} Repeat or skip this control.").ConfigureAwait(false);
                }
            }
        }

        DeviceDefinitionValidationResult validation = session.Validate();
        if (!validation.IsValid)
            throw new DeviceDefinitionFormatException(string.Join(Environment.NewLine, validation.Errors));
        DeviceDefinition preview = session.Preview();
        DeviceCalibration calibration = session.PreviewCalibration();
        string previewText = string.Join(Environment.NewLine, preview.Mappings.Select(mapping => $"  {mapping.Target} <- {mapping.RawControlId} ({mapping.Mode})"));
        await output.WriteLineAsync($"Preview (no files saved yet):{Environment.NewLine}{previewText}{Environment.NewLine}Validation: valid.").ConfigureAwait(false);
        await output.WriteLineAsync("Is the selected device a physical controller you personally tested? Type 'yes' to mark contributor-tested; otherwise the report is unverified.").ConfigureAwait(false);
        bool contributorTested = string.Equals((await input.ReadLineAsync().ConfigureAwait(false))?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        await output.WriteLineAsync("Save this local definition, per-unit calibration, and privacy-bounded report? Type 'yes' to save.").ConfigureAwait(false);
        if (!string.Equals((await input.ReadLineAsync().ConfigureAwait(false))?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
            return new("Preview complete. No local definition, calibration, or report was saved.");

        string root = DataRoot();
        SavedDeviceDefinition saved = new DeviceDefinitionStore(root).Save(preview, calibration with { UnitFingerprint = capture.LocalUnitFingerprint });
        HardwareReport report = HardwareReportGenerator.Generate(session, BuildVersion(), BuildCommit(), contributorTested ? "contributor-tested" : "unverified");
        string reportPath = Path.Combine(root, "reports", $"{session.Preview().Id}-{calibration.CalibrationId}.json");
        WriteAtomically(reportPath, HardwareReportJson.Serialize(report));
        WriteAtomically(Path.Combine(root, "reports", "latest.json"), HardwareReportJson.Serialize(report));
        return new(
            $"Teaching complete. Definition: {saved.DefinitionPath}{Environment.NewLine}Calibration: {saved.CalibrationPath}{Environment.NewLine}Privacy-bounded report: {reportPath}",
            new { definitionId = session.Preview().Id, calibrationId = calibration.CalibrationId, report = reportPath });
    }

    private static async Task<IReadOnlyList<RawInputSample>> CaptureStepAsync(
        WindowsHidDeviceEnumerator.WindowsHidInputCapture capture,
        TextReader input,
        TextWriter output)
    {
        using var stop = new CancellationTokenSource();
        Task<string?> stopLine = input.ReadLineAsync(stop.Token).AsTask();
        _ = stopLine.ContinueWith(_ => stop.Cancel(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var samples = new List<RawInputSample>();
        TimeSpan lastSample = TimeSpan.MinValue;
        try
        {
            await foreach (IReadOnlyList<RawInputSample> report in capture.ReadReportsAsync(stop.Token).ConfigureAwait(false))
            {
                TimeSpan timestamp = report.Count == 0 ? TimeSpan.Zero : report[0].Timestamp;
                if (lastSample != TimeSpan.MinValue && timestamp - lastSample < TimeSpan.FromMilliseconds(50))
                    continue;
                if (samples.Count + report.Count > ControlCalibration.MaximumSampleCount)
                    throw new InvalidOperationException("Capture sample limit reached. Repeat this control with a shorter capture window.");
                samples.AddRange(report);
                lastSample = timestamp;
            }
        }
        catch (OperationCanceledException) when (stopLine.IsCompleted)
        {
        }
        finally
        {
            stop.Cancel();
        }
        if (samples.Count < 2)
            throw new InvalidOperationException("No usable HID input observations were captured. Reconnect the selected controller and repeat.");
        await output.WriteLineAsync($"Captured {samples.Count} bounded raw observations.").ConfigureAwait(false);
        return samples.AsReadOnly();
    }

    private static CliResult Validate(string path)
    {
        string json = ReadBoundedTextFile(path, 1_000_000);
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("mappingCandidate", out _))
        {
            HardwareReport report = HardwareReportJson.Deserialize(json);
            return new("Hardware report is valid.", new { kind = "hardware-report", report.SchemaVersion, report.ControllerOSVersion, report.ControllerOSCommit, report.EvidenceLevel, report.Validation.IsValid });
        }

        ControllerProfile profile = ControllerProfileJson.Deserialize(json);
        return new("ControllerScript profile is valid.", new { kind = "controller-profile", profile.SchemaVersion, profile.Metadata.Name });
    }

    private static CliResult Simulate(string path)
    {
        ControllerProfile profile = ControllerProfileJson.LoadFile(path);
        var compiler = new ControllerScriptCompiler();
        ControllerProgram program = compiler.Compile(profile.Source, profile.Metadata.Name);
        var recorded = new RecordedOutput();
        var runtime = new ControllerRuntime(program, recorded);
        runtime.Start();
        SendSyntheticSouthPress(runtime);
        runtime.AdvanceTo(TimeSpan.FromMilliseconds(50));
        int diagnostics = runtime.Diagnostics.Count;
        runtime.Stop();
        return new($"Simulation completed: {program.Handlers.Count} handler(s), {recorded.Frames.Count} output frame(s), {diagnostics} diagnostic(s).",
            new { handlers = program.Handlers.Count, outputFrames = recorded.Frames.Count, diagnostics }, ExitCode: diagnostics == 0 ? SuccessCode : ErrorCode);
    }

    private static CliResult ExportReport(IReadOnlyList<string> arguments, bool jsonMode)
    {
        string input = arguments.Count > 0 ? arguments[0] : Path.Combine(DataRoot(), "reports", "latest.json");
        HardwareReport report = HardwareReportJson.Deserialize(ReadBoundedTextFile(input, 1_000_000));
        string safeJson = HardwareReportJson.Serialize(report);
        if (arguments.Count > 1)
        {
            WriteAtomically(arguments[1], safeJson);
            return new($"Privacy-bounded hardware report exported to {Path.GetFullPath(arguments[1])}.", new { report.EvidenceLevel, report.ControllerOSVersion, report.ControllerOSCommit });
        }
        return jsonMode
            ? new("Privacy-bounded hardware report validated.", report)
            : new(string.Empty, safeJson, WriteDataDirectly: true);
    }

    private static CliResult SelfTest(IWindowsDeviceEnumerator enumerator)
    {
        var checks = new List<SelfTestCheck>();
        var compiler = new ControllerScriptCompiler();
        ControllerProgram program = compiler.Compile("on press(SOUTH):\n    press(EAST)\n", "self-test");
        var recorded = new RecordedOutput();
        var runtime = new ControllerRuntime(program, recorded);
        runtime.Start();
        SendSyntheticSouthPress(runtime);
        runtime.AdvanceTo(TimeSpan.FromMilliseconds(50));
        bool runtimePass = runtime.Diagnostics.Count == 0 && recorded.Frames.Any(frame => frame.State.Values.TryGetValue(ControlId.EAST, out ControlValue value) && value.IsPressed);
        runtime.Stop();
        checks.Add(new("runtime-and-script", runtimePass, runtimePass ? "ControllerScript compiled and executed through the core runtime." : "Runtime execution did not produce the expected synthetic output."));

        DeviceTeachingSession teaching = CreateSyntheticTeachingSession();
        bool syntheticPass = teaching.Validate().IsValid;
        checks.Add(new("synthetic-teaching", syntheticPass, syntheticPass ? "Synthetic button, stick, trigger, calibration, and skip path passed." : "Synthetic teaching validation failed."));

        HardwareReport syntheticReport = HardwareReportGenerator.Generate(teaching, BuildVersion(), BuildCommit(), "mapping-only");
        bool reportPass = HardwareReportJson.Deserialize(HardwareReportJson.Serialize(syntheticReport)).Validation.IsValid;
        checks.Add(new("report-validation", reportPass, reportPass ? "Allowlisted report serialized and validated." : "Report validation failed."));

        bool isWindows = OperatingSystem.IsWindows();
        bool outputAvailable = isWindows && File.Exists(Path.Combine(AppContext.BaseDirectory, "HIDMaestro.Core.dll"));
        checks.Add(new("output-backend", !isWindows || outputAvailable,
            isWindows ? outputAvailable ? "Pinned HIDMaestro dependency is present; first activation requires an elevated Windows process." : "Pinned HIDMaestro.Core.dll is missing from the application directory." : "Windows-only virtual output is not applicable on this host."));

        bool enumerationPass = true;
        string enumerationMessage;
        if (!isWindows)
        {
            enumerationMessage = "Windows HID enumeration is not applicable on this host.";
        }
        else
        {
            try
            {
                int count = GetControllers(enumerator).Count;
                enumerationMessage = $"Windows HID enumeration is available; {count} generic gamepad or joystick collection(s) found.";
            }
            catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
                enumerationPass = false;
                enumerationMessage = $"Windows HID enumeration failed: {exception.Message}";
            }
        }
        checks.Add(new("device-enumeration", enumerationPass, enumerationMessage));

        bool passed = checks.All(check => check.Passed);
        string message = $"Self-test {(passed ? "passed" : "failed")}: {checks.Count(check => check.Passed)}/{checks.Count} checks passed." +
            Environment.NewLine + string.Join(Environment.NewLine, checks.Select(check => $"{(check.Passed ? "PASS" : "FAIL")} {check.Name}: {check.Message}"));
        return new(message, new { passed, checks }, ExitCode: passed ? SuccessCode : ErrorCode);
    }

    private static async Task<CliResult> StartRuntimeAsync(IWindowsDeviceEnumerator enumerator, IReadOnlyList<string> arguments, TextWriter output)
    {
        ControllerProfile profile = ControllerProfileJson.LoadFile(arguments[0]);
        string deviceId = arguments[2];
        string definitionId = arguments[4];
        string calibrationId = arguments[6];
        WindowsHidDevice device = SelectController(enumerator, deviceId);
        DeviceDefinitionStore store = new(DataRoot());
        DeviceDefinition definition = store.Load(definitionId);
        DeviceCalibration calibration = store.LoadCalibration(calibrationId);
        using WindowsHidDeviceEnumerator.WindowsHidInputCapture capture = ((WindowsHidDeviceEnumerator)enumerator).OpenInputCapture(device);
        if (!definition.Match.Matches(capture.Descriptor.Match))
            throw new InvalidOperationException("Selected HID controller does not match the saved local device definition.");
        if (!calibration.IsCompatibleWith(definition))
            throw new InvalidOperationException("Selected calibration does not match the saved device definition.");
        if (!calibration.MatchesUnit(capture.LocalUnitFingerprint))
            throw new InvalidOperationException("Selected calibration belongs to a different HID interface. Teach this unit or select its calibration ID.");

        var compiler = new ControllerScriptCompiler();
        ControllerProgram program = compiler.Compile(profile.Source, profile.Metadata.Name);
        var capabilities = new ControllerCapabilities(definition.Mappings.Select(mapping => mapping.Target));
        using VirtualXboxOutput backend = VirtualXboxOutput.Create();
        var runtime = new ControllerRuntime(program, backend, capabilities);
        runtime.Start();
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        Task stopListener = ListenForRuntimeStopAsync(stop);
        await output.WriteLineAsync("Runtime active. Use 'controlleros runtime stop' or Ctrl+C to stop safely.").ConfigureAwait(false);
        try
        {
            await RunRuntimeInputAsync(capture.ReadReportsAsync(stop.Token), () => capture.Timestamp, definition, calibration, runtime, stop)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            stop.Cancel();
            try
            {
                await stopListener.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            runtime.Stop();
        }
        return new("Runtime stopped and outputs were reset.");
    }

    internal static async Task RunRuntimeInputAsync(
        IAsyncEnumerable<IReadOnlyList<RawInputSample>> reportStream,
        Func<TimeSpan> getTimestamp,
        DeviceDefinition definition,
        DeviceCalibration calibration,
        ControllerRuntime runtime,
        CancellationTokenSource stop,
        TimeSpan? idleTick = null)
    {
        ArgumentNullException.ThrowIfNull(reportStream);
        ArgumentNullException.ThrowIfNull(getTimestamp);
        TimeSpan tickInterval = idleTick ?? TimeSpan.FromMilliseconds(16);
        if (tickInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTick));
        ControllerState snapshot = runtime.InputState;
        long sequence = 0;
        await using IAsyncEnumerator<IReadOnlyList<RawInputSample>> reports = reportStream.GetAsyncEnumerator(stop.Token);
        Task<bool> pendingReport = reports.MoveNextAsync().AsTask();
        try
        {
            while (runtime.IsRunning)
            {
                Task tick = Task.Delay(tickInterval, stop.Token);
                Task completed = await Task.WhenAny(pendingReport, tick).ConfigureAwait(false);
                if (stop.IsCancellationRequested)
                    break;

                TimeSpan timestamp = getTimestamp();
                if (completed == tick)
                {
                    await tick.ConfigureAwait(false);
                    runtime.AdvanceTo(timestamp);
                    continue;
                }

                if (!await pendingReport.ConfigureAwait(false))
                    break;

                var events = new List<ControllerInputEvent>();
                foreach (RawInputSample sample in reports.Current)
                {
                    DeviceControlMapping? mapping = definition.Mappings.SingleOrDefault(item => item.RawControlId == sample.ControlId);
                    if (mapping is null)
                        continue;
                    ControlValue value = DeviceDefinitionNormalizer.Normalize(definition, calibration, sample.ControlId, sample.Value);
                    ControlValue previous = snapshot.Get(mapping.Target);
                    if (previous == value)
                        continue;
                    ControllerState next = snapshot.WithValue(timestamp, value);
                    ControllerInputEvent? inputEvent = ControllerInputEvent.Create(timestamp, sequence, previous, value);
                    if (inputEvent is not null)
                    {
                        events.Add(inputEvent);
                        sequence++;
                    }
                    snapshot = next;
                }
                if (events.Count > 0)
                    runtime.ProcessInputBatch(events);
                else
                    runtime.AdvanceTo(timestamp);
                if (!runtime.IsRunning)
                    throw new InvalidOperationException("Runtime stopped after an output backend failure.");

                pendingReport = reports.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            stop.Cancel();
            try
            {
                await pendingReport.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task ListenForRuntimeStopAsync(CancellationTokenSource stop)
    {
        await using var pipe = new NamedPipeServerStream(RuntimeStopPipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        if (await reader.ReadLineAsync(stop.Token).ConfigureAwait(false) == "STOP")
            stop.Cancel();
    }

    private static async Task<CliResult> StopRuntimeAsync()
    {
        await using var pipe = new NamedPipeClientStream(".", RuntimeStopPipe, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync("STOP").ConfigureAwait(false);
        return new("Stop requested; the runtime will reset output and exit.");
    }

    private static void SendSyntheticSouthPress(ControllerRuntime runtime)
    {
        ControllerState previous = runtime.InputState;
        TimeSpan pressedAt = TimeSpan.FromMilliseconds(1);
        ControllerState pressed = previous.WithValue(pressedAt, ControlValue.Button(ControlId.SOUTH, true));
        ControllerInputEvent? inputEvent = ControllerInputEvent.Create(pressedAt, 0, previous.Get(ControlId.SOUTH), pressed.Get(ControlId.SOUTH));
        if (inputEvent is not null)
            runtime.ProcessInputBatch([inputEvent]);
    }

    private static DeviceTeachingSession CreateSyntheticTeachingSession()
    {
        var session = new DeviceTeachingSession(SyntheticUnknownDeviceFixture.Device);
        session.Observe(ControlId.SOUTH, SyntheticUnknownDeviceFixture.SouthButtonSamples());
        session.Observe(ControlId.LEFT_STICK_X, SyntheticUnknownDeviceFixture.LeftStickXSamples());
        session.Observe(ControlId.LEFT_STICK_Y, SyntheticUnknownDeviceFixture.LeftStickYSamples());
        session.Observe(ControlId.LEFT_TRIGGER, SyntheticUnknownDeviceFixture.TriggerSamples());
        foreach (ControlId control in ControlCatalog.All.Where(control => control is not (ControlId.SOUTH or ControlId.LEFT_STICK_X or ControlId.LEFT_STICK_Y or ControlId.LEFT_TRIGGER)))
            session.Skip(control);
        return session;
    }

    private static IReadOnlyList<WindowsHidDevice> GetControllers(IWindowsDeviceEnumerator enumerator) =>
        enumerator.Enumerate().Where(device => device.UsagePage == 0x01 && device.Usage is 0x04 or 0x05 &&
            device.VendorId is > 0 && device.ProductId is > 0 && device.InputReportBytes is > 0 &&
            (device.InputButtonCapabilityCount ?? 0) + (device.InputValueCapabilityCount ?? 0) > 0).ToArray();

    private static WindowsHidDevice SelectController(IWindowsDeviceEnumerator enumerator, string id)
    {
        int index;
        if (id.StartsWith("controller-", StringComparison.Ordinal) && int.TryParse(id.AsSpan("controller-".Length), out index))
        {
            // Controller IDs are one-based indexes from the current devices listing.
        }
        else if (!int.TryParse(id, out index))
        {
            throw new ArgumentException("Controller ID must be a one-based number from 'controlleros devices'.");
        }
        IReadOnlyList<WindowsHidDevice> devices = GetControllers(enumerator);
        if (index < 1 || index > devices.Count)
            throw new ArgumentException("Controller ID is not present in the current HID enumeration; run 'controlleros devices' again.");
        return devices[index - 1];
    }

    private static string ControllerId(int zeroBasedIndex) => $"controller-{zeroBasedIndex + 1:000}";

    private static string DisplayName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 120 && !value.Any(char.IsControl) ? value : "HID controller";

    private static string FormatHex(ushort? value) => value is null ? "unknown" : $"{value.Value:X4}";

    private static string TeachingPrompt(ControlId control) => control switch
    {
        ControlId.LEFT_STICK_X => "Move LEFT STICK horizontally to both limits, then return it to center.",
        ControlId.LEFT_STICK_Y => "Move LEFT STICK vertically to both limits, then return it to center.",
        ControlId.RIGHT_STICK_X => "Move RIGHT STICK horizontally to both limits, then return it to center.",
        ControlId.RIGHT_STICK_Y => "Move RIGHT STICK vertically to both limits, then return it to center.",
        ControlId.LEFT_TRIGGER => "Squeeze LEFT TRIGGER to its limit, then release it.",
        ControlId.RIGHT_TRIGGER => "Squeeze RIGHT TRIGGER to its limit, then release it.",
        ControlId.DPAD_UP => "Press D-pad UP, then return it to neutral.",
        ControlId.DPAD_DOWN => "Press D-pad DOWN, then return it to neutral.",
        ControlId.DPAD_LEFT => "Press D-pad LEFT, then return it to neutral.",
        ControlId.DPAD_RIGHT => "Press D-pad RIGHT, then return it to neutral.",
        _ => $"Press {control}, then release it."
    };

    private static string DataRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerOS");

    private static string BuildVersion()
    {
        string value = typeof(ControllerOsCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.2.0-alpha.1";
        int metadata = value.IndexOf('+');
        return metadata < 0 ? value : value[..metadata];
    }

    private static string BuildCommit() => typeof(ControllerOsCli).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "ControllerOSCommit")?.Value ?? "unknown";

    private static string ReadBoundedTextFile(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes)
            throw new ArgumentException($"Input file exceeds the {maximumBytes}-byte limit.", nameof(path));
        byte[] bytes = new byte[maximumBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0)
                break;
            count += read;
        }
        if (count > maximumBytes)
            throw new ArgumentException($"Input file exceeds the {maximumBytes}-byte limit.", nameof(path));
        return new UTF8Encoding(false, true).GetString(bytes, 0, count);
    }

    private static void WriteAtomically(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? throw new IOException("Unable to resolve output directory.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static async Task WriteFailureAsync(string command, int code, string message, bool json, TextWriter output, TextWriter error)
    {
        if (json)
            await output.WriteLineAsync(JsonSerializer.Serialize(new { success = false, command, exitCode = code, error = message }, JsonOptions)).ConfigureAwait(false);
        else
            await error.WriteLineAsync($"{message} (exit {code})").ConfigureAwait(false);
    }

    private sealed record CliResult(string Message, object? Data = null, bool WriteDataDirectly = false, int ExitCode = SuccessCode);
    private sealed record DeviceListItem(string Id, string Name, ushort VendorId, ushort ProductId, ushort? Revision,
        string ConnectionMode, ushort UsagePage, ushort Usage, ushort? InputReportBytes, ushort? InputButtonCapabilityCount, ushort? InputValueCapabilityCount);
    private sealed record SelfTestCheck(string Name, bool Passed, string Message);
}
