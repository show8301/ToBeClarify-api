using System.Diagnostics;
using Microsoft.Extensions.Options;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Services.Logging;

namespace ToBeClarify.Api.Middlewares;

public sealed class RequestInterceptorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestInterceptorMiddleware> _logger;
    private readonly IAppClock _clock;
    private readonly ApiLoggingOptions _options;

    public RequestInterceptorMiddleware(
        RequestDelegate next,
        ILogger<RequestInterceptorMiddleware> logger,
        IAppClock clock,
        IOptions<ApiLoggingOptions> options)
    {
        _next = next;
        _logger = logger;
        _clock = clock;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, IApiLogService apiLogService)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await LogRequestAsync(context, apiLogService, stopwatch.ElapsedMilliseconds, exception);
        }
    }

    public static string GetClientIpAddress(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            return forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

    public static string GetApiType(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase)) return "admin";
        return "client";
    }

    private async Task LogRequestAsync(HttpContext context, IApiLogService apiLogService, long elapsedMilliseconds, Exception? exception)
    {
        try
        {
            var statusCode = exception is null
                ? context.Response.StatusCode
                : (int)ExceptionHandlingMiddleware.MapException(exception).StatusCode;
            var entry = new ApiLogEntry
            {
                RequestTime = _clock.LocalDateTime,
                Level = GetLevel(statusCode, elapsedMilliseconds),
                IpAddress = GetClientIpAddress(context),
                DeviceInfo = context.Request.Headers.UserAgent.ToString(),
                ApiType = GetApiType(context.Request.Path),
                Method = context.Request.Method,
                Path = context.Request.Path.Value ?? string.Empty,
                StatusCode = statusCode,
                DurationMs = elapsedMilliseconds > int.MaxValue ? int.MaxValue : (int)elapsedMilliseconds,
                ExceptionMessage = exception is null
                    ? null
                    : statusCode >= 500 ? exception.ToString() : exception.Message,
                UserId = context.User.Identity?.IsAuthenticated == true
                    ? context.User.FindFirst("sub")?.Value ?? context.User.Identity.Name
                    : null
            };

            await apiLogService.LogAsync(entry, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request interceptor failed to write request log.");
        }
    }

    private string GetLevel(int statusCode, long elapsedMilliseconds)
    {
        if (statusCode >= 500) return "ERROR";
        if (statusCode >= 400 || elapsedMilliseconds >= _options.SlowRequestThresholdMs) return "WARNING";
        return "INFORMATION";
    }
}
