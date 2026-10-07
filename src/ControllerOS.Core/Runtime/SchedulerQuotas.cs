namespace ControllerOS.Core.Runtime;

public sealed record SchedulerQuotas(int MaximumTasks = 64, int MaximumTimers = 128, int InstructionBudgetPerSlice = 10_000)
{
    public SchedulerQuotas Validate()
    {
        if (MaximumTasks <= 0 || MaximumTimers <= 0 || InstructionBudgetPerSlice <= 0)
            throw new ArgumentOutOfRangeException(nameof(SchedulerQuotas), "Scheduler quotas must be positive.");
        return this;
    }
}
