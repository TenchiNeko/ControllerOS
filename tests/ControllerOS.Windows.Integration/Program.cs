using System.Diagnostics;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Devices;
using ControllerOS.Core.Input;
using ControllerOS.Windows;

if (args.Contains("--expect-missing-backend", StringComparer.Ordinal))
{
    try
    {
        using VirtualXboxOutput _ = VirtualXboxOutput.Create();
        throw new InvalidOperationException("The backend started even though its SDK DLL should be absent.");
    }
    catch (InvalidOperationException error) when (error.Message.Contains("HIDMaestro.Core.dll is missing", StringComparison.Ordinal))
    {
        Console.WriteLine($"PASS: {error.Message}");
        return 0;
    }
}

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Run this integration check on Windows with administrator rights.");

var enumerator = new WindowsHidDeviceEnumerator();
IReadOnlyList<WindowsHidDevice> before = enumerator.Enumerate();
WindowsControllerInput[] inputSlots = Enumerable.Range(0, 4).Select(slot => new WindowsControllerInput(slot)).ToArray();
foreach (WindowsControllerInput input in inputSlots)
{
    if (input.Capabilities.Supports(ControlId.GUIDE))
        throw new InvalidOperationException("Documented XInput polling must not advertise the inaccessible Guide control.");
    _ = input.Poll();
}
TimeSpan previousPoll = inputSlots[0].Snapshot.Timestamp;
_ = inputSlots[0].Poll();
if (inputSlots[0].Snapshot.Timestamp <= previousPoll)
    throw new InvalidOperationException("XInput poll timestamps must increase strictly, even when a poll occurs within one clock tick.");
bool[] connectedBefore = inputSlots.Select(input => input.IsConnected).ToArray();
VirtualXboxOutput output = VirtualXboxOutput.Create();
try
{
    IReadOnlyList<WindowsHidDevice> active = enumerator.Enumerate();
    WindowsHidDevice? virtualDevice = active.FirstOrDefault(device =>
        !before.Any(existing => existing.DevicePath == device.DevicePath) &&
        device.VendorId == 0x045E && device.ProductId == 0x028E);
    if (virtualDevice is null || virtualDevice.InputReportBytes is null or 0)
        throw new InvalidOperationException("HID discovery did not report the new Xbox 360 virtual device and its input report size.");

    using WindowsHidDeviceEnumerator.WindowsHidInputCapture rawCapture = enumerator.OpenInputCapture(virtualDevice);
    using var rawCaptureStop = new CancellationTokenSource();
    var rawBaselineReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    Task<IReadOnlyList<RawInputSample>> rawTransitionTask = WaitForRawHidTransitionAsync(rawCapture, rawBaselineReady, rawCaptureStop.Token);
    await rawBaselineReady.Task.WaitAsync(TimeSpan.FromSeconds(5));

    WindowsControllerInput input = WaitForNewInput(inputSlots, connectedBefore, TimeSpan.FromSeconds(5));
    OutputState action = OutputState.Neutral
        .WithValue(ControlValue.Button(ControlId.EAST, pressed: true))
        .WithValue(ControlValue.Trigger(ControlId.LEFT_TRIGGER, 0.75))
        .WithValue(ControlValue.Axis(ControlId.LEFT_STICK_X, 0.5));
    output.Commit(TimeSpan.FromMilliseconds(1), action);
    IReadOnlyList<RawInputSample> rawTransition = await rawTransitionTask.WaitAsync(TimeSpan.FromSeconds(5));
    rawCaptureStop.Cancel();
    if (rawTransition.Count == 0)
        throw new InvalidOperationException("Selected virtual gamepad produced an empty raw HID input report.");
    ControllerInputEvent pressed = WaitForTransition(input, ControlId.EAST, InputEventKind.Press, TimeSpan.FromSeconds(5));
    ControllerState activeState = input.Snapshot;
    if (!activeState.Get(ControlId.EAST).IsPressed || Math.Abs(activeState.Get(ControlId.LEFT_TRIGGER).Value - 0.75) > 0.01 || Math.Abs(activeState.Get(ControlId.LEFT_STICK_X).Value - 0.5) > 0.01)
        throw new InvalidOperationException("The XInput source did not return the normalized button, trigger, and axis values.");
    Console.WriteLine($"PASS: discovered {virtualDevice.Product}, VID:PID {virtualDevice.VendorId:X4}:{virtualDevice.ProductId:X4}, usage {virtualDevice.UsagePage:X4}:{virtualDevice.Usage:X4}");
    Console.WriteLine($"PASS: selected HID capture decoded {rawTransition.Count} raw control observations without exporting its device path");
    Console.WriteLine($"PASS: XInput slot {input.UserIndex} returned EAST press, LEFT_TRIGGER 0.75, and LEFT_STICK_X 0.50");

    output.Dispose();
    ControllerInputEvent released = WaitForTransition(input, ControlId.EAST, InputEventKind.Release, TimeSpan.FromSeconds(5));
    if (released.Timestamp < pressed.Timestamp || released.Sequence <= pressed.Sequence || input.Snapshot.Get(ControlId.LEFT_TRIGGER).Value != 0 || input.Snapshot.Get(ControlId.LEFT_STICK_X).Value != 0)
        throw new InvalidOperationException("Disconnect did not release the held button and return analog controls to neutral with monotonic events.");
    IReadOnlyList<WindowsHidDevice> afterDispose = enumerator.Enumerate();
    if (afterDispose.Any(device => device.DevicePath == virtualDevice.DevicePath))
        throw new InvalidOperationException("Disposing the output backend did not remove its virtual HID interface.");
    Console.WriteLine("PASS: XInput disconnect emitted release/neutral transitions with monotonic timestamps");
    Console.WriteLine("PASS: disposing the backend removed its virtual HID interface");
}
finally
{
    output.Dispose();
}

return 0;

static WindowsControllerInput WaitForNewInput(WindowsControllerInput[] inputs, bool[] connectedBefore, TimeSpan timeout)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < timeout)
    {
        foreach (WindowsControllerInput input in inputs)
        {
            _ = input.Poll();
            if (!connectedBefore[input.UserIndex] && input.IsConnected)
                return input;
        }

        Thread.Sleep(25);
    }

    throw new InvalidOperationException("No new XInput slot appeared. Ensure the VM has a free controller slot.");
}

static async Task<IReadOnlyList<RawInputSample>> WaitForRawHidTransitionAsync(
    WindowsHidDeviceEnumerator.WindowsHidInputCapture capture,
    TaskCompletionSource baselineReady,
    CancellationToken cancellationToken)
{
    var previous = new Dictionary<string, double>(StringComparer.Ordinal);
    await foreach (IReadOnlyList<RawInputSample> report in capture.ReadReportsAsync(cancellationToken))
    {
        bool changed = report.Any(sample => previous.TryGetValue(sample.ControlId, out double value) && value != sample.Value);
        foreach (RawInputSample sample in report)
            previous[sample.ControlId] = sample.Value;
        if (previous.Count > 0)
            baselineReady.TrySetResult();
        if (changed)
            return report;
    }
    throw new EndOfStreamException("Selected virtual HID interface disconnected during raw capture.");
}

static ControllerInputEvent WaitForTransition(WindowsControllerInput input, ControlId control, InputEventKind kind, TimeSpan timeout)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < timeout)
    {
        IReadOnlyList<ControllerInputEvent> events = input.Poll();
        ControllerInputEvent? transition = events.FirstOrDefault(inputEvent => inputEvent.Control == control && inputEvent.Kind == kind);
        if (transition is not null)
            return transition;
        Thread.Sleep(25);
    }

    throw new InvalidOperationException($"XInput did not report {kind} for {control} within {timeout.TotalSeconds:0.##} seconds.");
}
