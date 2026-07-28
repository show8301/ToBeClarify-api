using System.Net;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Common;

namespace ToBeClarify.Api.Middlewares;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var traceId = context.TraceIdentifier;
            var (statusCode, errorCode, message) = MapException(ex);
            if ((int)statusCode >= 500)
                _logger.LogError(ex, "Unhandled API exception. TraceId: {TraceId}", traceId);
            else
                _logger.LogWarning("API request rejected. ErrorCode: {ErrorCode}, TraceId: {TraceId}", errorCode, traceId);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            var response = ApiResponse<object>.Fail(errorCode, message, traceId);
            await context.Response.WriteAsJsonAsync(response);
        }
    }

    public static (HttpStatusCode StatusCode, string ErrorCode, string Message) MapException(Exception exception)
    {
        return exception switch
        {
            BusinessException businessException => (HttpStatusCode.BadRequest, businessException.ErrorCode, businessException.Message),
            NotFoundException notFoundException => (HttpStatusCode.NotFound, notFoundException.ErrorCode, notFoundException.Message),
            ForbiddenException forbiddenException => (HttpStatusCode.Forbidden, forbiddenException.ErrorCode, forbiddenException.Message),
            UnauthorizedException unauthorizedException => (HttpStatusCode.Unauthorized, unauthorizedException.ErrorCode, "Unauthorized"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized"),
            _ => (HttpStatusCode.InternalServerError, "SERVER_ERROR", "系統發生錯誤，請稍後再試")
        };
    }
}
