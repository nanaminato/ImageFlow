namespace ImageFlow;

public sealed class QueueGate { public SemaphoreSlim Mutex { get; } = new(1, 1); }
