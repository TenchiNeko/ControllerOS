namespace ControllerOS.Core.Runtime;

public sealed class DeterministicScheduler
{
    private readonly SchedulerQuotas quotas;
    private readonly Queue<TaskEntry> ready = new();
    private readonly PriorityQueue<TaskEntry, (long DueTicks, long Sequence)> timers = new();
    private readonly HashSet<TaskEntry> active = [];
    private long timerSequence;
    private bool hasDispatched;
    private TimeSpan lastDispatchTimestamp;

    public TimeSpan Now { get; private set; }
    public event Action<SchedulerDiagnostic>? Diagnostic;

    public DeterministicScheduler(SchedulerQuotas? quotas = null)
    {
        this.quotas = (quotas ?? new SchedulerQuotas()).Validate();
    }

    public int ActiveTaskCount => active.Count;
    public int ActiveTimerCount => timers.Count;

    public void Schedule(IScheduledTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        EnsureTaskCapacity();
        var entry = new TaskEntry(task);
        active.Add(entry);
        ready.Enqueue(entry);
    }

    public void ScheduleAfter(IScheduledTask task, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));
        EnsureTaskCapacity();
        if (timers.Count >= quotas.MaximumTimers)
            throw new SchedulerQuotaExceededException($"Active timer quota exceeded ({quotas.MaximumTimers}).");

        long dueTicks = checked(Now.Ticks + delay.Ticks);
        var entry = new TaskEntry(task) { Waiting = true };
        active.Add(entry);
        timers.Enqueue(entry, (dueTicks, timerSequence++));
    }

    public void DispatchAt(TimeSpan timestamp, Action dispatchInputs, Action<TimeSpan> commitOutput)
    {
        ArgumentNullException.ThrowIfNull(dispatchInputs);
        ArgumentNullException.ThrowIfNull(commitOutput);
        if (hasDispatched && timestamp == lastDispatchTimestamp)
            throw new ArgumentException("Submit all input events for one logical timestamp in a single batch.", nameof(timestamp));
        ValidateNextTimestamp(timestamp);

        DrainTimersBefore(timestamp, commitOutput);
        Now = timestamp;
        hasDispatched = true;
        lastDispatchTimestamp = timestamp;
        dispatchInputs();
        RunReady();
        RunTimersAt(timestamp);
        commitOutput(timestamp);
    }

    public void AdvanceTo(TimeSpan timestamp, Action<TimeSpan> commitOutput)
    {
        ArgumentNullException.ThrowIfNull(commitOutput);
        ValidateNextTimestamp(timestamp);

        // Leave timers due exactly at timestamp for DispatchAt so input at that
        // timestamp is processed before timers, independent of caller ordering.
        DrainTimersBefore(timestamp, commitOutput);
        Now = timestamp;
    }

    public void Complete(Action<TimeSpan> commitOutput)
    {
        ArgumentNullException.ThrowIfNull(commitOutput);
        while (timers.TryPeek(out _, out (long DueTicks, long Sequence) priority))
        {
            TimeSpan due = TimeSpan.FromTicks(priority.DueTicks);
            Now = due;
            RunTimersAt(due);
            commitOutput(due);
        }
        RunReady();
    }

    public void CancelAll()
    {
        foreach (TaskEntry entry in active)
        {
            try
            {
                entry.Task.Cancel();
            }
            catch (Exception exception)
            {
                Report(entry.Task, "Cancellation failed.", exception);
            }
        }
        active.Clear();
        ready.Clear();
        timers.Clear();
    }

    public void CancelHandlers(IReadOnlySet<string> handlerNames)
    {
        ArgumentNullException.ThrowIfNull(handlerNames);
        foreach (TaskEntry entry in active.Where(entry => handlerNames.Contains(entry.Task.HandlerName)).ToArray())
        {
            entry.Active = false;
            active.Remove(entry);
            try
            {
                entry.Task.Cancel();
            }
            catch (Exception exception)
            {
                Report(entry.Task, "Cancellation failed.", exception);
            }
        }

        RebuildQueues();
    }

    private void DrainTimersBefore(TimeSpan timestamp, Action<TimeSpan> commitOutput)
    {
        while (timers.TryPeek(out _, out (long DueTicks, long Sequence) priority) && priority.DueTicks < timestamp.Ticks)
        {
            TimeSpan due = TimeSpan.FromTicks(priority.DueTicks);
            Now = due;
            RunTimersAt(due);
            commitOutput(due);
        }
    }

    private void RunTimersAt(TimeSpan timestamp)
    {
        RunReady();
        while (timers.TryPeek(out _, out (long DueTicks, long Sequence) priority) && priority.DueTicks <= timestamp.Ticks)
        {
            TaskEntry entry = timers.Dequeue();
            if (!entry.Waiting)
                continue;
            entry.Waiting = false;
            Run(entry);
            RunReady();
        }
    }

    private void RunReady()
    {
        while (ready.TryDequeue(out TaskEntry? entry))
        {
            if (!entry.Active)
                continue;
            Run(entry);
        }
    }

    private void Run(TaskEntry entry)
    {
        try
        {
            SchedulerStep step = entry.Task.RunSlice(Now, quotas.InstructionBudgetPerSlice);
            if (step.Completed)
            {
                CompleteTask(entry);
                return;
            }

            if (step.Delay <= TimeSpan.Zero)
                throw new InvalidOperationException("A scheduled continuation must yield for a positive duration.");

            if (timers.Count >= quotas.MaximumTimers)
                throw new SchedulerQuotaExceededException($"Active timer quota exceeded ({quotas.MaximumTimers}).");

            TimeSpan due = Now + step.Delay;
            if (due < Now)
                throw new SchedulerQuotaExceededException("Scheduled time overflowed the logical clock.");

            entry.Waiting = true;
            timers.Enqueue(entry, (due.Ticks, timerSequence++));
        }
        catch (Exception exception)
        {
            CompleteTask(entry);
            try
            {
                entry.Task.Cancel();
            }
            catch (Exception cancelException)
            {
                Report(entry.Task, "Cancellation after task failure also failed.", cancelException);
            }
            Report(entry.Task, exception is SchedulerQuotaExceededException ? exception.Message : $"Task failed: {exception.Message}", exception);
        }
    }

    private void CompleteTask(TaskEntry entry)
    {
        entry.Active = false;
        entry.Waiting = false;
        active.Remove(entry);
    }

    private void EnsureTaskCapacity()
    {
        if (active.Count >= quotas.MaximumTasks)
            throw new SchedulerQuotaExceededException($"Active task quota exceeded ({quotas.MaximumTasks}).");
    }

    private void ValidateNextTimestamp(TimeSpan timestamp)
    {
        if (timestamp < TimeSpan.Zero || timestamp < Now)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "Scheduler timestamps must be nonnegative and monotonic.");
    }

    private void Report(IScheduledTask task, string message, Exception exception) =>
        Report(new SchedulerDiagnostic(Now, task.HandlerName, message, exception));

    private void Report(SchedulerDiagnostic diagnostic)
    {
        if (Diagnostic is null)
            return;

        foreach (Action<SchedulerDiagnostic> listener in Diagnostic.GetInvocationList().Cast<Action<SchedulerDiagnostic>>())
        {
            try
            {
                listener(diagnostic);
            }
            catch (Exception)
            {
                // Diagnostics are advisory; a failing observer cannot stop controller processing.
            }
        }
    }

    private void RebuildQueues()
    {
        TaskEntry[] readyEntries = ready.Where(entry => entry.Active).ToArray();
        ready.Clear();
        foreach (TaskEntry entry in readyEntries)
            ready.Enqueue(entry);

        var timerEntries = timers.UnorderedItems.Where(item => item.Element.Active).ToArray();
        timers.Clear();
        foreach (var item in timerEntries)
            timers.Enqueue(item.Element, item.Priority);
    }

    private sealed class TaskEntry(IScheduledTask task)
    {
        public IScheduledTask Task { get; } = task;
        public bool Active { get; set; } = true;
        public bool Waiting { get; set; }
    }
}
