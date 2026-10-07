using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ControllerOS.Core.ControllerScript;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Devices;
using ControllerOS.Core.Input;
using ControllerOS.Core.Output;
using ControllerOS.Core.Profiles;
using ControllerOS.Core.Reports;
using ControllerOS.Core.Simulation;
using ControllerOS.Windows;
using Microsoft.Win32;

namespace ControllerOS.Desktop;

public partial class MainWindow : Window
{
    private const string ApplicationVersion = "0.1.0-alpha.1";
    private const string DefaultScript = """
        state enabled = true

        on press(SOUTH):
            if enabled and LEFT_TRIGGER > 0.5:
                press(EAST)
                wait(100ms)
                release(EAST)
        """;

    private readonly Stopwatch inputClock = Stopwatch.StartNew();
    private readonly KeyboardTestInput keyboardInput = new();
    private readonly DispatcherTimer runtimeTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(4) };
    private readonly ObservableCollection<string> inputLines = [];
    private readonly ObservableCollection<string> outputLines = [];
    private readonly DeviceDefinitionStore deviceStore = new(GetDataDirectory());
    private ControllerRuntime? runtime;
    private DesktopOutputSink? runtimeOutput;
    private WindowsControllerInput? xInputInput;
    private bool runtimeUsesXInput;
    private DeviceTeachingSession? teachingSession;

    public MainWindow()
    {
        InitializeComponent();
        ScriptEditor.Text = DefaultScript;
        InputInspector.ItemsSource = inputLines;
        OutputInspector.ItemsSource = outputLines;
        TeachingControlBox.ItemsSource = ControlCatalog.All;
        TeachingControlBox.SelectedItem = ControlId.SOUTH;
        runtimeTimer.Tick += RuntimeTimer_Tick;
        RenderInput(keyboardInput.Snapshot(TimeSpan.Zero));
        RenderOutput(OutputState.Neutral);
        RefreshSelectedInput();
        Loaded += (_, _) => RefreshDevices();
    }

    private static string GetDataDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerOS");

    private void Compile_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsList.Items.Clear();
        try
        {
            _ = CompileEditor();
            StatusText.Text = "Compile succeeded.";
            AddDiagnostic("Compile succeeded.");
        }
        catch (ControllerScriptException exception)
        {
            ShowScriptErrors(exception);
        }
        catch (Exception exception)
        {
            AddDiagnostic(exception.Message);
            StatusText.Text = "Compile failed.";
        }
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ControllerOS profile (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            ControllerProfile profile = ControllerProfileJson.LoadFile(dialog.FileName);
            ProfileNameBox.Text = profile.Metadata.Name;
            ScriptEditor.Text = profile.Source;
            DiagnosticsList.Items.Clear();
            StatusText.Text = $"Loaded '{profile.Metadata.Name}'.";
            AddDiagnostic("Profile loaded and validated.");
        }
        catch (ControllerProfileFormatException exception)
        {
            AddDiagnostic($"Profile load failed ({exception.Code}): {exception.Message}");
            StatusText.Text = "Profile load failed.";
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Profile load failed: {exception.Message}");
            StatusText.Text = "Profile load failed.";
        }
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "ControllerOS profile (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = SafeFileName(ProfileNameBox.Text) + ".json"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var profile = new ControllerProfile(
                ControllerProfile.CurrentSchemaVersion,
                new ControllerProfileMetadata(NormalizedProfileName()),
                ScriptEditor.Text);
            ControllerProfileJson.SaveFile(dialog.FileName, profile);
            StatusText.Text = "Profile saved.";
            AddDiagnostic("Profile saved and validated.");
        }
        catch (ControllerProfileFormatException exception)
        {
            AddDiagnostic($"Profile save failed ({exception.Code}): {exception.Message}");
            StatusText.Text = "Profile save failed.";
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Profile save failed: {exception.Message}");
            StatusText.Text = "Profile save failed.";
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartRuntime();

    private void StartRuntime()
    {
        ControllerProgram program;
        try
        {
            program = CompileEditor();
        }
        catch (ControllerScriptException exception)
        {
            ShowScriptErrors(exception);
            return;
        }
        catch (Exception exception)
        {
            AddDiagnostic(exception.Message);
            StatusText.Text = "Profile did not start.";
            return;
        }

        StopRuntime();
        DiagnosticsList.Items.Clear();
        IOutputSink? backend = null;
        string backendStatus = "Debug preview output active.";
        if (VirtualOutputEnabled.IsChecked == true)
        {
            try
            {
                backend = VirtualXboxOutput.Create();
                backendStatus = "Xbox virtual output active.";
            }
            catch (Exception exception)
            {
                backendStatus = $"Virtual Xbox unavailable; using debug preview. {exception.Message}";
                AddDiagnostic(backendStatus);
            }
        }

        runtimeUsesXInput = SelectedXInputUserIndex() is int;
        if (runtimeUsesXInput)
        {
            try
            {
                xInputInput = new WindowsControllerInput(SelectedXInputUserIndex()!.Value);
            }
            catch (Exception exception)
            {
                (backend as IDisposable)?.Dispose();
                AddDiagnostic($"XInput source could not start: {exception.Message}");
                StatusText.Text = "Profile did not start.";
                runtimeUsesXInput = false;
                UpdateRuntimeTimer();
                return;
            }
        }

        ControllerCapabilities inputCapabilities = runtimeUsesXInput
            ? xInputInput!.Capabilities
            : keyboardInput.Capabilities;
        runtimeOutput = new DesktopOutputSink(backend, RenderOutput);
        try
        {
            runtime = new ControllerRuntime(program, runtimeOutput, inputCapabilities);
            runtime.Diagnostic += OnRuntimeDiagnostic;
            runtime.Start();
            if (!runtime.IsRunning)
            {
                StatusText.Text = "Output adapter failed during startup; the profile was disabled.";
                runtimeOutput.Dispose();
                runtime = null;
                runtimeOutput = null;
                runtimeUsesXInput = false;
                UpdateRuntimeTimer();
                return;
            }
            InputSourceBox.IsEnabled = false;
            runtimeTimer.Start();
            StatusText.Text = $"Running '{program.Name}'. {backendStatus}";
            RenderSelectedInput();
            AddDiagnostic("Profile started. Use Disable now or Escape to release every output.");
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Profile startup failed: {exception.Message}");
            StatusText.Text = "Profile did not start.";
            runtimeOutput?.Dispose();
            runtime = null;
            runtimeOutput = null;
            runtimeUsesXInput = false;
            InputSourceBox.IsEnabled = true;
            UpdateRuntimeTimer();
        }
    }

    private ControllerProgram CompileEditor() =>
        new ControllerScriptCompiler().Compile(ScriptEditor.Text, NormalizedProfileName());

    private string NormalizedProfileName() => string.IsNullOrWhiteSpace(ProfileNameBox.Text)
        ? "Untitled profile"
        : ProfileNameBox.Text.Trim();

    private void Stop_Click(object sender, RoutedEventArgs e) => StopRuntime();

    private void Disable_Click(object sender, RoutedEventArgs e)
    {
        StopRuntime();
        StatusText.Text = "Profile disabled; output is neutral.";
    }

    private void StopRuntime()
    {
        runtimeTimer.Stop();
        TimeSpan timestamp = inputClock.Elapsed;
        IReadOnlyList<ControllerInputEvent> releases = keyboardInput.ReleaseAll(timestamp);
        if (!runtimeUsesXInput && runtime is { IsRunning: true } && releases.Count > 0)
        {
            try
            {
                runtime.ProcessInputBatch(releases);
            }
            catch (Exception exception)
            {
                AddDiagnostic($"Input release failed: {exception.Message}");
            }
        }

        try
        {
            runtime?.Stop();
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Stopping the runtime failed: {exception.Message}");
        }
        runtime = null;
        try
        {
            runtimeOutput?.Dispose();
        }
        catch (Exception exception)
        {
            AddDiagnostic($"Closing the output backend failed: {exception.Message}");
        }
        runtimeOutput = null;
        runtimeUsesXInput = false;
        InputSourceBox.IsEnabled = true;
        RenderSelectedInput(timestamp);
        RenderOutput(OutputState.Neutral);
        UpdateRuntimeTimer();
    }

    private void RuntimeTimer_Tick(object? sender, EventArgs e)
    {
        if (SelectedXInputUserIndex() is int userIndex)
        {
            try
            {
                if (xInputInput is null || xInputInput.UserIndex != userIndex)
                    xInputInput = new WindowsControllerInput(userIndex);

                IReadOnlyList<ControllerInputEvent> events = xInputInput.Poll();
                ControllerState snapshot = xInputInput.Snapshot;
                if (runtime is { IsRunning: true } activeRuntime && runtimeUsesXInput)
                {
                    if (events.Count > 0)
                        activeRuntime.ProcessInputBatch(events);
                    activeRuntime.AdvanceTo(snapshot.Timestamp);
                }

                RenderInput(snapshot);
                UpdateInputInspectorHeader();
            }
            catch (Exception exception)
            {
                runtimeTimer.Stop();
                AddDiagnostic($"XInput polling failed: {exception.Message}");
            }

            return;
        }

        if (runtime is not { IsRunning: true } activeKeyboardRuntime)
        {
            runtimeTimer.Stop();
            return;
        }

        try
        {
            TimeSpan timestamp = inputClock.Elapsed;
            activeKeyboardRuntime.AdvanceTo(timestamp);
            RenderInput(keyboardInput.Snapshot(timestamp));
            UpdateInputInspectorHeader();
        }
        catch (Exception exception)
        {
            runtimeTimer.Stop();
            AddDiagnostic($"Advancing runtime time failed: {exception.Message}");
        }
    }

    private void OnRuntimeDiagnostic(RuntimeDiagnostic diagnostic)
    {
        string source = diagnostic.Source is { } span ? $" at {span}" : string.Empty;
        AddDiagnostic($"{diagnostic.Timestamp.TotalMilliseconds:0.###} ms · {diagnostic.Handler}{source}: {diagnostic.Message}");
        if (diagnostic.Disabled)
            StatusText.Text = "A handler was disabled after a runtime failure; use Disable now to recover.";
    }

    private void ShowScriptErrors(ControllerScriptException exception)
    {
        DiagnosticsList.Items.Clear();
        foreach (ScriptDiagnostic diagnostic in exception.Diagnostics)
            AddDiagnostic(diagnostic.ToString());
        StatusText.Text = "Compile failed; the active runtime was not changed.";
    }

    private void AddDiagnostic(string message)
    {
        DiagnosticsList.Items.Add(message);
        while (DiagnosticsList.Items.Count > 200)
            DiagnosticsList.Items.RemoveAt(0);
        DiagnosticsList.ScrollIntoView(message);
    }

    private void RenderInput(ControllerState state)
    {
        inputLines.Clear();
        foreach (ControlValue value in state.Values.Values)
        {
            string display = value.Kind switch
            {
                ControlKind.Button => value.IsPressed ? "pressed" : "released",
                _ => value.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
            };
            inputLines.Add($"{value.Id,-20} {display}");
        }
    }

    private void InputSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;
        if (runtime is { IsRunning: true })
            return;

        RefreshSelectedInput();
    }

    private void RefreshSelectedInput()
    {
        int? userIndex = SelectedXInputUserIndex();
        try
        {
            xInputInput = userIndex is int slot ? new WindowsControllerInput(slot) : null;
        }
        catch (Exception exception)
        {
            AddDiagnostic($"XInput source could not start: {exception.Message}");
            xInputInput = null;
        }
        RenderSelectedInput();
        UpdateInputInspectorHeader();
        UpdateRuntimeTimer();
    }

    private int? SelectedXInputUserIndex() =>
        InputSourceBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int userIndex)
            ? userIndex
            : null;

    private void RenderSelectedInput(TimeSpan? keyboardTimestamp = null)
    {
        if (SelectedXInputUserIndex() is not null && xInputInput is not null)
            RenderInput(xInputInput.Snapshot);
        else
            RenderInput(keyboardInput.Snapshot(keyboardTimestamp ?? inputClock.Elapsed));
    }

    private void UpdateInputInspectorHeader()
    {
        InputInspectorHeader.Text = SelectedXInputUserIndex() is int userIndex
            ? $"Live normalized input (XInput slot {userIndex}: {(xInputInput?.IsConnected == true ? "connected" : "disconnected")})"
            : "Live normalized input (keyboard test input)";
    }

    private void UpdateRuntimeTimer()
    {
        if (SelectedXInputUserIndex() is not null || runtime is { IsRunning: true })
            runtimeTimer.Start();
        else
            runtimeTimer.Stop();
    }

    private void RenderOutput(OutputState state)
    {
        outputLines.Clear();
        foreach (ControlValue value in state.Values.Values)
        {
            string display = value.Kind == ControlKind.Button
                ? value.IsPressed ? "pressed" : "released"
                : value.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            outputLines.Add($"{value.Id,-20} {display}");
        }
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => RefreshDevices();

    private void RefreshDevices()
    {
        DeviceList.Items.Clear();
        try
        {
            IReadOnlyList<WindowsHidDevice> devices = new WindowsHidDeviceEnumerator().Enumerate();
            foreach (WindowsHidDevice device in devices)
            {
                string support = "unknown";
                if (device.VendorId is ushort vendor && device.ProductId is ushort product &&
                    device.UsagePage is ushort usagePage && device.Usage is ushort usage)
                {
                    DeviceDefinition? definition = deviceStore.FindMatch(new DeviceMatchRule(vendor, product, usagePage, usage), out _);
                    support = definition is null ? "unknown" : $"known: {definition.DisplayName}";
                }

                string label = string.Join(" · ", new[]
                {
                    string.IsNullOrWhiteSpace(device.Manufacturer) ? null : device.Manufacturer,
                    string.IsNullOrWhiteSpace(device.Product) ? "HID device" : device.Product,
                    device.VendorId is ushort idVendor && device.ProductId is ushort idProduct ? $"VID {idVendor:X4} / PID {idProduct:X4}" : "IDs unavailable",
                    support
                }.Where(value => value is not null));
                DeviceList.Items.Add(label);
            }

            DeviceStatusText.Text = devices.Count == 0
                ? "No HID interfaces found; keyboard test input remains available."
                : $"Found {devices.Count} HID interface(s). Device paths stay local.";
        }
        catch (Exception exception)
        {
            DeviceStatusText.Text = $"Device discovery failed: {exception.Message}";
        }
    }

    private void OpenTeaching_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;

    private void LoadTeachingFixture_Click(object sender, RoutedEventArgs e)
    {
        teachingSession = new DeviceTeachingSession(SyntheticUnknownDeviceFixture.Device);
        TeachingControlBox.SelectedItem = ControlId.SOUTH;
        TeachingPreviewBox.Clear();
        TeachingStatusText.Text = "Synthetic device loaded. Observe each control or mark unavailable controls as skipped.";
    }

    private void ObserveControl_Click(object sender, RoutedEventArgs e)
    {
        if (teachingSession is null)
        {
            TeachingStatusText.Text = "Load a teaching device first.";
            return;
        }
        if (TeachingControlBox.SelectedItem is not ControlId control)
        {
            TeachingStatusText.Text = "Select a standard control.";
            return;
        }

        IReadOnlyList<RawInputSample>? samples = SamplesFor(control);
        if (samples is null)
        {
            TeachingStatusText.Text = $"The synthetic fixture has no sample for {control}; use Skip control to model an unavailable control.";
            return;
        }

        try
        {
            teachingSession.Observe(control, samples);
            TeachingStatusText.Text = $"Identified {control} from raw samples. {TeachingProgress()}";
        }
        catch (Exception exception)
        {
            TeachingStatusText.Text = exception.Message;
        }
    }

    private static IReadOnlyList<RawInputSample>? SamplesFor(ControlId control) => control switch
    {
        ControlId.SOUTH => SyntheticUnknownDeviceFixture.SouthButtonSamples(),
        ControlId.LEFT_STICK_X => SyntheticUnknownDeviceFixture.LeftStickXSamples(),
        ControlId.LEFT_STICK_Y => SyntheticUnknownDeviceFixture.LeftStickYSamples(),
        ControlId.LEFT_TRIGGER => SyntheticUnknownDeviceFixture.TriggerSamples(),
        _ => null
    };

    private void SkipControl_Click(object sender, RoutedEventArgs e)
    {
        if (teachingSession is null || TeachingControlBox.SelectedItem is not ControlId control)
        {
            TeachingStatusText.Text = "Load a teaching device and select a standard control first.";
            return;
        }

        try
        {
            teachingSession.Skip(control);
            TeachingStatusText.Text = $"Skipped {control}. {TeachingProgress()}";
        }
        catch (Exception exception)
        {
            TeachingStatusText.Text = exception.Message;
        }
    }

    private void PreviewTeaching_Click(object sender, RoutedEventArgs e)
    {
        if (teachingSession is null)
        {
            TeachingStatusText.Text = "Load a teaching device first.";
            return;
        }

        try
        {
            DeviceDefinition definition = teachingSession.Preview();
            string calibration = JsonSerializer.Serialize(teachingSession.PreviewCalibration(), new JsonSerializerOptions { WriteIndented = true });
            TeachingPreviewBox.Text = $"Mapping candidate\n{DeviceDefinitionJson.Serialize(definition)}\n\nPer-unit calibration\n{calibration}";
            TeachingStatusText.Text = $"Mapping preview is valid. {TeachingProgress()}";
        }
        catch (Exception exception)
        {
            TeachingStatusText.Text = exception.Message;
        }
    }

    private void SaveTeaching_Click(object sender, RoutedEventArgs e)
    {
        if (teachingSession is null)
        {
            TeachingStatusText.Text = "Load a teaching device first.";
            return;
        }

        try
        {
            SavedDeviceDefinition saved = teachingSession.Save(deviceStore);
            TeachingStatusText.Text = $"Saved reusable mapping and separate per-unit calibration locally: {saved.DefinitionPath}";
            RefreshDevices();
        }
        catch (Exception exception)
        {
            TeachingStatusText.Text = exception.Message;
        }
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (teachingSession is null)
        {
            TeachingStatusText.Text = "Load and complete a teaching session before exporting a report.";
            return;
        }

        try
        {
            string report = HardwareReportJson.Serialize(HardwareReportGenerator.Generate(teachingSession, ApplicationVersion));
            var dialog = new SaveFileDialog
            {
                Filter = "ControllerOS hardware report (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = "controlleros-hardware-report.json"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            File.WriteAllText(dialog.FileName, report, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TeachingStatusText.Text = "Sanitized hardware report exported. Review it before sharing.";
        }
        catch (Exception exception)
        {
            TeachingStatusText.Text = $"Report export failed: {exception.Message}";
        }
    }

    private string TeachingProgress() => teachingSession is null
        ? string.Empty
        : $"Identified {teachingSession.IdentifiedControls.Count}, skipped {teachingSession.SkippedControls.Count} of {ControlCatalog.All.Count}.";

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && runtime is { IsRunning: true } && !IsEditingText())
        {
            e.Handled = true;
            StopRuntime();
            StatusText.Text = "Profile disabled with Escape; output is neutral.";
            return;
        }

        if (SelectedXInputUserIndex() is not null || runtime is not { IsRunning: true } || IsEditingText())
            return;

        string key = KeyName(e);
        if (!KeyboardTestInput.SupportsKey(key))
            return;

        e.Handled = true;
        TimeSpan timestamp = inputClock.Elapsed;
        ProcessKeyboardEvents(keyboardInput.SetKey(key, isDown: true, timestamp), timestamp);
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (SelectedXInputUserIndex() is not null || runtime is not { IsRunning: true })
            return;

        string key = KeyName(e);
        if (!KeyboardTestInput.SupportsKey(key))
            return;

        e.Handled = true;
        TimeSpan timestamp = inputClock.Elapsed;
        ProcessKeyboardEvents(keyboardInput.SetKey(key, isDown: false, timestamp), timestamp);
    }

    private void ProcessKeyboardEvents(IReadOnlyList<ControllerInputEvent> events, TimeSpan timestamp)
    {
        if (!runtimeUsesXInput && runtime is { IsRunning: true } && events.Count > 0)
        {
            try
            {
                runtime.ProcessInputBatch(events);
            }
            catch (Exception exception)
            {
                AddDiagnostic($"Keyboard input failed: {exception.Message}");
            }
        }
        RenderInput(keyboardInput.Snapshot(timestamp));
    }

    private void Window_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is TextBoxBase or ComboBox)
            ReleaseHeldKeys();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => ReleaseHeldKeys();

    private void ReleaseHeldKeys()
    {
        TimeSpan timestamp = inputClock.Elapsed;
        ProcessKeyboardEvents(keyboardInput.ReleaseAll(timestamp), timestamp);
    }

    private void Window_Closed(object? sender, EventArgs e) => StopRuntime();

    private bool IsEditingText() => Keyboard.FocusedElement is TextBoxBase or ComboBox;

    private static string KeyName(KeyEventArgs e) => (e.Key == Key.System ? e.SystemKey : e.Key).ToString();

    private static string SafeFileName(string name)
    {
        string result = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(result) ? "controller-profile" : result;
    }

    private sealed class DesktopOutputSink(IOutputSink? backend, Action<OutputState> render) : IOutputSink, IDisposable
    {
        private bool disposed;

        public void Commit(TimeSpan timestamp, OutputState state)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            backend?.Commit(timestamp, state);
            render(state);
        }

        public void Reset(TimeSpan timestamp) => Commit(timestamp, OutputState.Neutral);

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            (backend as IDisposable)?.Dispose();
        }
    }
}
