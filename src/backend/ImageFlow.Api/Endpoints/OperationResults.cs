namespace ImageFlow;

public static class OperationResults
{
    public static IResult ToHttp<T>(this OperationResult<T> result, Func<T, string>? location = null) where T : class => result.Failure switch
    {
        FailureKind.InvalidInput => Results.BadRequest(new { error = result.Message }),
        FailureKind.NotFound => Results.NotFound(),
        FailureKind.Conflict => Results.Conflict(new { error = result.Message }),
        FailureKind.Provider => Results.Problem(result.Message, statusCode: 502),
        _ => result.Created && location != null ? Results.Created(location(result.Value!), result.Value) : Results.Ok(result.Value)
    };
}
