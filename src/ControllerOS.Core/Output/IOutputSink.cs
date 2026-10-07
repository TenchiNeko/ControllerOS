using ControllerOS.Core.Controls;

namespace ControllerOS.Core.Output;

public interface IOutputSink
{
    void Commit(TimeSpan timestamp, OutputState state);
    void Reset(TimeSpan timestamp);
}

public sealed record OutputFrame(TimeSpan Timestamp, long Sequence, OutputState State);
