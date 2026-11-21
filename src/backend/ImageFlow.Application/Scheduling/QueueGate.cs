namespace ImageFlow;

public sealed class QueueGate
{
    public SemaphoreSlim Mutex { get; } = new(1, 1);
    // Access only while holding Mutex; planning must settle before clearing the workspace.
    public int ActivePlanningRequests { get; set; }
}
