using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Output;

public sealed class RecordedOutput : IOutputSink
{
    private readonly List<OutputFrame> frames = [];
    private TimeSpan lastTimestamp = TimeSpan.Zero;

    public IReadOnlyList<OutputFrame> Frames => frames.AsReadOnly();

    public void Commit(TimeSpan timestamp, OutputState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (timestamp < lastTimestamp)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Output timestamps cannot move backwards.");
        frames.Add(new OutputFrame(timestamp, frames.Count, state));
        lastTimestamp = timestamp;
    }

    public void Reset(TimeSpan timestamp) => Commit(timestamp, OutputState.Neutral);
}

public sealed class DebugOutput(TextWriter writer) : IOutputSink
{
    public void Commit(TimeSpan timestamp, OutputState state)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(state);
        writer.WriteLine($"{timestamp.Ticks}: {string.Join(", ", state.Values.Values.Where(value => value.Value != 0).Select(value => $"{value.Id}={value.Value:0.###}"))}");
    }

    public void Reset(TimeSpan timestamp) => Commit(timestamp, OutputState.Neutral);
}
