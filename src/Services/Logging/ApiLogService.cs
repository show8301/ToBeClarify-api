using Dapper;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Services.Logging;

public sealed class ApiLogService : IApiLogService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ApiLogService> _logger;

    public ApiLogService(AppDbContext dbContext, ILogger<ApiLogService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogAsync(ApiLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            const string sql = """
                INSERT INTO `API_LOGS`
                    (`REQUEST_TIME`, `LEVEL`, `IP_ADDRESS`, `DEVICE_INFO`, `API_TYPE`, `METHOD`, `PATH`,
                     `STATUS_CODE`, `DURATION_MS`, `USER_ID`, `EXCEPTION_MESSAGE`)
                VALUES
                    (@RequestTime, @Level, @IpAddress, @DeviceInfo, @ApiType, @Method, @Path,
                     @StatusCode, @DurationMs, @UserId, @ExceptionMessage);
                """;

            await using var connection = await _dbContext.CreateOpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, entry, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write API log to database. Falling back to file.");
            await FallbackWriteToFileAsync(entry, cancellationToken);
        }
    }

    private async Task FallbackWriteToFileAsync(ApiLogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var line = string.Join('\t',
                entry.RequestTime.ToString("O"),
                entry.Level,
                entry.IpAddress,
                entry.ApiType,
                entry.Method,
                entry.Path,
                entry.StatusCode?.ToString() ?? string.Empty,
                entry.DurationMs.ToString(),
                entry.UserId ?? string.Empty,
                entry.ExceptionMessage ?? string.Empty);

            var logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);

            var filePath = Path.Combine(logDirectory, $"fallback_{entry.RequestTime:yyyyMMdd}.txt");
            await File.AppendAllTextAsync(filePath, line + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write fallback API log file.");
        }
    }
}
