using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Input;

public sealed record InputSample(TimeSpan Timestamp, ControlValue Value);

public sealed class SyntheticInput : IInputSource
{
    private readonly InputSample[] samples;

    public ControllerCapabilities Capabilities { get; }

    public SyntheticInput(ControllerCapabilities capabilities, IEnumerable<InputSample> samples)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(samples);
        Capabilities = capabilities;
        this.samples = samples.Take(100_001).ToArray();
        if (this.samples.Length > 100_000)
            throw new ArgumentException("Synthetic input is limited to 100,000 samples.", nameof(samples));

        TimeSpan previous = TimeSpan.Zero;
        foreach (InputSample sample in this.samples)
        {
            if (sample.Timestamp < previous)
                throw new ArgumentException("Synthetic input samples must be ordered by timestamp.", nameof(samples));
            if (sample.Timestamp < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(samples), "Timestamps cannot be negative.");
            if (!capabilities.Supports(sample.Value.Id))
                throw new ArgumentException($"Sample uses unsupported control {sample.Value.Id}.", nameof(samples));
            previous = sample.Timestamp;
        }
    }

    public IEnumerable<ControllerInputEvent> ReadEvents()
    {
        var state = ControllerState.Neutral(TimeSpan.Zero, Capabilities);
        long sequence = 0;
        foreach (InputSample sample in samples)
        {
            ControlValue previous = state.Get(sample.Value.Id);
            ControllerInputEvent? change = ControllerInputEvent.Create(sample.Timestamp, sequence, previous, sample.Value);
            if (change is null)
                continue;

            sequence++;
            state = state.WithValue(sample.Timestamp, sample.Value);
            yield return change;
        }
    }
}
