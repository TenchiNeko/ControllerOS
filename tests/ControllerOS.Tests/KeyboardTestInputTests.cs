using ControllerOS.Core.ControllerScript;
using ControllerOS.Core.Controls;
using ControllerOS.Core.Input;
using ControllerOS.Core.Output;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ControllerOS.Tests;

[TestClass]
public sealed class KeyboardTestInputTests
{
    [TestMethod]
    public void KeyboardKeysProduceNormalizedEventsAndSuppressKeyRepeat()
    {
        var input = new KeyboardTestInput();
        TimeSpan timestamp = TimeSpan.FromMilliseconds(5);

        IReadOnlyList<ControllerInputEvent> down = input.SetKey("Space", true, timestamp);
        Assert.AreEqual(1, down.Count);
        Assert.AreEqual(InputEventKind.Press, down[0].Kind);
        Assert.AreEqual(ControlId.SOUTH, down[0].Control);
        Assert.AreEqual(0, input.SetKey("Space", true, timestamp).Count);
        Assert.AreEqual(InputEventKind.Release, input.SetKey("Space", false, timestamp)[0].Kind);
    }

    [TestMethod]
    public void DirectionalKeysRecomputeAxesAndOppositesCancel()
    {
        var input = new KeyboardTestInput();
        Assert.AreEqual(1.0, input.SetKey("D", true, TimeSpan.Zero).Single().Current.Value);
        IReadOnlyList<ControllerInputEvent> diagonal = input.SetKey("W", true, TimeSpan.Zero);
        Assert.AreEqual(1.0, diagonal.Single(item => item.Control == ControlId.LEFT_STICK_Y).Current.Value);
        Assert.AreEqual(0.0, input.SetKey("A", true, TimeSpan.FromMilliseconds(1)).Single(item => item.Control == ControlId.LEFT_STICK_X).Current.Value);
        Assert.AreEqual(1.0, input.Snapshot(TimeSpan.FromMilliseconds(1)).Get(ControlId.LEFT_STICK_Y).Value);
    }

    [TestMethod]
    public void KeyboardEventsCanDriveTheSameControllerRuntime()
    {
        const string source = "on press(SOUTH):\n    press(EAST)\n";
        var keyboard = new KeyboardTestInput();
        var output = new RecordedOutput();
        var runtime = new ControllerRuntime(new ControllerScriptCompiler().Compile(source), output, keyboard.Capabilities);
        runtime.Start();

        runtime.ProcessInputBatch(keyboard.SetKey("Space", true, TimeSpan.FromMilliseconds(10)));

        OutputFrame frame = output.Frames.Single(item => item.Timestamp == TimeSpan.FromMilliseconds(10));
        Assert.IsTrue(frame.State.Get(ControlId.EAST).IsPressed);
    }

    [TestMethod]
    public void UnsupportedKeysAreIgnoredAndTimeCannotMoveBackwards()
    {
        var input = new KeyboardTestInput();
        Assert.AreEqual(0, input.SetKey("NotAKey", true, TimeSpan.Zero).Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => input.SetKey("Space", true, TimeSpan.FromMilliseconds(-1)));
    }

    [TestMethod]
    public void ReleaseAllClearsHeldControlsInOneOrderedBatch()
    {
        var input = new KeyboardTestInput();
        input.SetKey("Space", true, TimeSpan.Zero);
        input.SetKey("D", true, TimeSpan.Zero);

        IReadOnlyList<ControllerInputEvent> released = input.ReleaseAll(TimeSpan.FromMilliseconds(1));

        CollectionAssert.AreEqual(new[] { ControlId.SOUTH, ControlId.LEFT_STICK_X }, released.Select(item => item.Control).ToArray());
        Assert.IsFalse(input.Snapshot(TimeSpan.FromMilliseconds(1)).Get(ControlId.SOUTH).IsPressed);
        Assert.AreEqual(0.0, input.Snapshot(TimeSpan.FromMilliseconds(1)).Get(ControlId.LEFT_STICK_X).Value);
    }
}
