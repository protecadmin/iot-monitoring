using System.Text.Json;
using Bms.Api.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Bms.Api.Middleware;

/// <summary>
/// Centralized exception handling — see docs/api/api-design.md. Every
/// unhandled exception becomes the same {success,message,errorCode,traceId}
/// shape rather than a raw stack trace or an inconsistent per-controller
/// try/catch.
/// </summary>
public static class ExceptionHandlingMiddleware
{
    public static void UseBmsExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(handler =>
        {
            handler.Run(async context =>
            {
                var feature = context.Features.Get<IExceptionHandlerFeature>();
                var exception = feature?.Error;
                var traceId = context.TraceIdentifier;

                var (statusCode, message, errorCode) = exception switch
                {
                    ApiException apiEx => (apiEx.StatusCode, apiEx.Message, apiEx.ErrorCode),
                    UnauthorizedAccessException => (401, "Unauthorized", "UNAUTHORIZED"),
                    _ => (500, "An unexpected error occurred", "INTERNAL_ERROR")
                };

                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ExceptionHandling");
                if (statusCode >= 500)
                    logger.LogError(exception, "Unhandled exception, traceId {TraceId}", traceId);
                else
                    logger.LogWarning("Request error {ErrorCode}: {Message}, traceId {TraceId}", errorCode, message, traceId);

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                var body = ApiError.Create(message, errorCode, traceId);
                // Web defaults (camelCase) like every other response — the
                // dashboard reads body.message, so PascalCase hid the reason.
                await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
            });
        });
    }
}
