using ControllerOS.Core.Controls;
using ControllerOS.Core.Output;
using HIDMaestro;

namespace ControllerOS.Windows;

internal sealed class HidMaestroVirtualOutput : IOutputSink, IDisposable
{
    private readonly object gate = new();
    private readonly HMContext context;
    private readonly HMController controller;
    private TimeSpan lastTimestamp;
    private bool hasCommitted;
    private bool disposed;

    private HidMaestroVirtualOutput()
    {
        HMContext? createdContext = null;
        try
        {
            createdContext = new HMContext();
            createdContext.LoadDefaultProfiles();
            HMProfile profile = createdContext.GetProfile("xbox-360-wired")
                ?? throw new InvalidOperationException("HIDMaestro v1.11.0 does not include the xbox-360-wired profile.");

            if (!createdContext.IsDriverInstalled)
                createdContext.InstallDriver();

            context = createdContext;
            controller = context.CreateController(profile);
            Commit(TimeSpan.Zero, OutputState.Neutral);
        }
        catch (UnauthorizedAccessException error)
        {
            DisposeFailedContext(createdContext);
            throw new InvalidOperationException(
                "Starting Xbox virtual output requires an elevated administrator process to install and create its Windows device. Close ControllerOS, then use Run as administrator.",
                error);
        }
        catch (Exception error)
        {
            DisposeFailedContext(createdContext);
            throw new InvalidOperationException(
                $"Could not start the HIDMaestro Xbox virtual output. Confirm Windows device installation is allowed and the application is running as administrator. Details: {error.Message}",
                error);
        }
    }

    public void Commit(TimeSpan timestamp, OutputState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (timestamp < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timestamp));

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (hasCommitted && timestamp < lastTimestamp)
                throw new ArgumentOutOfRangeException(nameof(timestamp), "Virtual output timestamps cannot move backwards.");

            controller.SubmitState(ToHidMaestroState(controller.Profile, state));
            lastTimestamp = timestamp;
            hasCommitted = true;
        }
    }

    public void Reset(TimeSpan timestamp) => Commit(timestamp, OutputState.Neutral);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            try
            {
                controller.SubmitState(ToHidMaestroState(controller.Profile, OutputState.Neutral));
            }
            finally
            {
                disposed = true;
                context.Dispose();
            }
        }
    }

    private static HMGamepadState ToHidMaestroState(HMProfile profile, OutputState state)
    {
        HMButton buttons = HMButton.None;
        AddButton(ControlId.SOUTH, HMButton.A);
        AddButton(ControlId.EAST, HMButton.B);
        AddButton(ControlId.WEST, HMButton.X);
        AddButton(ControlId.NORTH, HMButton.Y);
        AddButton(ControlId.LEFT_BUMPER, HMButton.LeftBumper);
        AddButton(ControlId.RIGHT_BUMPER, HMButton.RightBumper);
        AddButton(ControlId.LEFT_STICK_CLICK, HMButton.LeftStick);
        AddButton(ControlId.RIGHT_STICK_CLICK, HMButton.RightStick);
        AddButton(ControlId.MENU, HMButton.Start);
        AddButton(ControlId.VIEW, HMButton.Back);
        AddButton(ControlId.GUIDE, HMButton.Guide);

        int horizontal = Pressed(ControlId.DPAD_RIGHT) ? 1 : 0;
        horizontal -= Pressed(ControlId.DPAD_LEFT) ? 1 : 0;
        int vertical = Pressed(ControlId.DPAD_UP) ? 1 : 0;
        vertical -= Pressed(ControlId.DPAD_DOWN) ? 1 : 0;
        HMHat hat = (horizontal, vertical) switch
        {
            (0, 1) => HMHat.North,
            (1, 1) => HMHat.NorthEast,
            (1, 0) => HMHat.East,
            (1, -1) => HMHat.SouthEast,
            (0, -1) => HMHat.South,
            (-1, -1) => HMHat.SouthWest,
            (-1, 0) => HMHat.West,
            (-1, 1) => HMHat.NorthWest,
            _ => HMHat.None
        };

        return new HMGamepadState
        {
            Axes = HMGamepadStateHelpers.StandardAxes(
                profile,
                leftStickX: Axis(ControlId.LEFT_STICK_X),
                leftStickY: Axis(ControlId.LEFT_STICK_Y),
                rightStickX: Axis(ControlId.RIGHT_STICK_X),
                rightStickY: Axis(ControlId.RIGHT_STICK_Y),
                leftTrigger: (float)state.Get(ControlId.LEFT_TRIGGER).Value,
                rightTrigger: (float)state.Get(ControlId.RIGHT_TRIGGER).Value),
            Buttons = buttons,
            Hat = hat
        };

        void AddButton(ControlId id, HMButton button)
        {
            if (Pressed(id))
                buttons |= button;
        }

        bool Pressed(ControlId id) => state.Get(id).IsPressed;
        float Axis(ControlId id) => (float)((state.Get(id).Value + 1.0) * 0.5);
    }

    private static void DisposeFailedContext(HMContext? context)
    {
        try { context?.Dispose(); }
        catch { /* Preserve the startup diagnostic; context disposal is best-effort after partial startup. */ }
    }
}
