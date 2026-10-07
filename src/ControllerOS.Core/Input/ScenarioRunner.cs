using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Input;

public sealed record ScenarioResult(IReadOnlyList<ControllerInputEvent> Events, ControllerState FinalState);

public static class ScenarioRunner
{
    public static ScenarioResult Capture(IInputSource input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var events = new List<ControllerInputEvent>();
        foreach (ControllerInputEvent inputEvent in input.ReadEvents())
        {
            if (events.Count >= 100_000)
                throw new InvalidOperationException("Scenario capture is limited to 100,000 input events.");
            events.Add(inputEvent);
        }
        var state = ControllerState.Neutral(TimeSpan.Zero, input.Capabilities);
        foreach (ControllerInputEvent inputEvent in events)
        {
            if (inputEvent.Timestamp < state.Timestamp)
                throw new InvalidOperationException("Input source returned events out of timestamp order.");
            state = state.WithValue(inputEvent.Timestamp, inputEvent.Current);
        }
        return new ScenarioResult(Array.AsReadOnly(events.ToArray()), state);
    }
}
