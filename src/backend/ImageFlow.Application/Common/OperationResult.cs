namespace ImageFlow;

public enum FailureKind { InvalidInput, NotFound, Conflict, Provider }
public record OperationResult<T>(T? Value = default, FailureKind? Failure = null, string? Message = null, bool Created = false) where T : class;
