using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Input;

namespace ControllerOS.Windows;

/// <summary>Polls one XInput slot and turns its state changes into normalized controller events.</summary>
public sealed class WindowsControllerInput : IInputSource
{
    private const uint DeviceNotConnected = 1167;
    private static readonly ControllerCapabilities XInputCapabilities = new(ControlCatalog.All.Where(control => control != ControlId.GUIDE));
    private readonly object gate = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private ControllerState snapshot;
    private bool connected;
    private TimeSpan lastTimestamp;
    private long nextSequence;

    public int UserIndex { get; }
    public ControllerCapabilities Capabilities { get; } = XInputCapabilities;
    public bool IsConnected
    {
        get
        {
            lock (gate)
                return connected;
        }
    }

    public WindowsControllerInput(int userIndex)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("XInput controller input is available only on Windows.");
        if (userIndex is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(userIndex), "XInput user index must be between 0 and 3.");

        UserIndex = userIndex;
        snapshot = ControllerState.Neutral(TimeSpan.Zero, Capabilities);
    }

    /// <summary>
    /// Polls the selected XInput slot once. Button and axis changes, including
    /// releases after disconnect, are returned in stable control order.
    /// </summary>
    public IReadOnlyList<ControllerInputEvent> Poll()
    {
        lock (gate)
        {
            TimeSpan timestamp = NextTimestamp();
            uint result = Native.XInputGetState((uint)UserIndex, out XInputState state);
            bool connected = result == 0;
            if (!connected && result != DeviceNotConnected)
                throw new Win32Exception(unchecked((int)result), $"XInputGetState failed for user index {UserIndex}.");

            ControllerState nextState = connected
                ? new ControllerState(timestamp, Capabilities, ReadValues(state.Gamepad))
                : ControllerState.Neutral(timestamp, Capabilities);
            var events = new List<ControllerInputEvent>();
            foreach (ControlId control in Capabilities.Controls.Order())
            {
                ControlValue previous = snapshot.Get(control);
                ControlValue current = nextState.Get(control);
                ControllerInputEvent? transition = ControllerInputEvent.Create(timestamp, nextSequence, previous, current);
                if (transition is null)
                    continue;

                events.Add(transition);
                nextSequence++;
            }

            snapshot = nextState;
            lastTimestamp = timestamp;
            this.connected = connected;
            return events.AsReadOnly();
        }
    }

    /// <summary>Reads one polling batch; call again to continue monitoring the slot.</summary>
    public IEnumerable<ControllerInputEvent> ReadEvents() => Poll();

    public ControllerState Snapshot
    {
        get
        {
            lock (gate)
                return snapshot;
        }
    }

    private TimeSpan NextTimestamp() => TimeSpan.FromTicks(Math.Max(clock.Elapsed.Ticks, checked(lastTimestamp.Ticks + 1)));

    private static IEnumerable<ControlValue> ReadValues(XInputGamepad gamepad)
    {
        ushort buttons = gamepad.Buttons;
        yield return ControlValue.Button(ControlId.SOUTH, IsPressed(buttons, 0x1000));
        yield return ControlValue.Button(ControlId.EAST, IsPressed(buttons, 0x2000));
        yield return ControlValue.Button(ControlId.WEST, IsPressed(buttons, 0x4000));
        yield return ControlValue.Button(ControlId.NORTH, IsPressed(buttons, 0x8000));
        yield return ControlValue.Button(ControlId.LEFT_BUMPER, IsPressed(buttons, 0x0100));
        yield return ControlValue.Button(ControlId.RIGHT_BUMPER, IsPressed(buttons, 0x0200));
        yield return ControlValue.Trigger(ControlId.LEFT_TRIGGER, gamepad.LeftTrigger / 255.0);
        yield return ControlValue.Trigger(ControlId.RIGHT_TRIGGER, gamepad.RightTrigger / 255.0);
        yield return ControlValue.Axis(ControlId.LEFT_STICK_X, NormalizeAxis(gamepad.LeftThumbX));
        yield return ControlValue.Axis(ControlId.LEFT_STICK_Y, NormalizeAxis(gamepad.LeftThumbY));
        yield return ControlValue.Axis(ControlId.RIGHT_STICK_X, NormalizeAxis(gamepad.RightThumbX));
        yield return ControlValue.Axis(ControlId.RIGHT_STICK_Y, NormalizeAxis(gamepad.RightThumbY));
        yield return ControlValue.Button(ControlId.LEFT_STICK_CLICK, IsPressed(buttons, 0x0040));
        yield return ControlValue.Button(ControlId.RIGHT_STICK_CLICK, IsPressed(buttons, 0x0080));
        yield return ControlValue.Button(ControlId.DPAD_UP, IsPressed(buttons, 0x0001));
        yield return ControlValue.Button(ControlId.DPAD_DOWN, IsPressed(buttons, 0x0002));
        yield return ControlValue.Button(ControlId.DPAD_LEFT, IsPressed(buttons, 0x0004));
        yield return ControlValue.Button(ControlId.DPAD_RIGHT, IsPressed(buttons, 0x0008));
        yield return ControlValue.Button(ControlId.MENU, IsPressed(buttons, 0x0010));
        yield return ControlValue.Button(ControlId.VIEW, IsPressed(buttons, 0x0020));
    }

    private static bool IsPressed(ushort buttons, ushort mask) => (buttons & mask) != 0;

    private static double NormalizeAxis(short value) => value < 0
        ? value / 32768.0
        : value / 32767.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    private static class Native
    {
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        internal static extern uint XInputGetState(uint userIndex, out XInputState state);
    }
}
