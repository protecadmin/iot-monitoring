namespace Bms.Api.Common;

public sealed record ApiError(bool Success, string Message, string ErrorCode, string TraceId)
{
    public static ApiError Create(string message, string errorCode, string traceId) =>
        new(false, message, errorCode, traceId);
}

public sealed class ApiException(int statusCode, string message, string errorCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;

    public static ApiException NotFound(string message, string errorCode = "NOT_FOUND") => new(404, message, errorCode);
    public static ApiException BadRequest(string message, string errorCode = "BAD_REQUEST") => new(400, message, errorCode);
    public static ApiException Conflict(string message, string errorCode = "CONFLICT") => new(409, message, errorCode);
    public static ApiException Forbidden(string message, string errorCode = "FORBIDDEN") => new(403, message, errorCode);
    public static ApiException Unprocessable(string message, string errorCode = "UNPROCESSABLE") => new(422, message, errorCode);
}
