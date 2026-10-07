namespace ControllerOS.Core.Runtime;

public readonly record struct SchedulerStep(bool Completed, TimeSpan Delay)
{
    public static SchedulerStep Complete() => new(true, TimeSpan.Zero);

    public static SchedulerStep Wait(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "A scheduled wait must have a positive duration.");
        return new(false, delay);
    }
}

public interface IScheduledTask
{
    string HandlerName { get; }
    SchedulerStep RunSlice(TimeSpan now, int instructionBudget);
    void Cancel();
}

public sealed record SchedulerDiagnostic(TimeSpan Timestamp, string HandlerName, string Message, Exception? Exception = null);

public sealed class SchedulerQuotaExceededException(string message) : InvalidOperationException(message);
