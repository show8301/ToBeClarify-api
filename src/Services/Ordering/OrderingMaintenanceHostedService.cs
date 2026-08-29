namespace ToBeClarify.Api.Services.Ordering;

public sealed class OrderingMaintenanceHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderingMaintenanceHostedService> _logger;

    public OrderingMaintenanceHostedService(IServiceScopeFactory scopeFactory,
        ILogger<OrderingMaintenanceHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IOrderingService>();
                await service.ExpireWaitingOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Some restricted Windows hosts cannot write to Event Log. Queue
                // maintenance must keep retrying even when the logger provider fails.
                try { _logger.LogError(ex, "Ordering queue maintenance failed."); }
                catch { }
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
