using Microsoft.Extensions.DependencyInjection;

namespace ToBeClarify.Api.Services.Media;

public sealed class MediaMaintenanceHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaMaintenanceHostedService> _logger;

    public MediaMaintenanceHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<MediaMaintenanceHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediaService = scope.ServiceProvider.GetRequiredService<AdminMediaUploadService>();
                var migrated = await mediaService.NormalizeMonthlyPathsAsync(stoppingToken);
                var cleaned = await mediaService.CleanupExpiredUnreferencedAsync(stoppingToken);
                if (migrated > 0 || cleaned > 0)
                    _logger.LogInformation("Media maintenance completed. Migrated {MigratedCount} paths and cleaned {CleanedCount} unreferenced assets.", migrated, cleaned);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media maintenance failed; it will retry on the next cycle.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
