using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Ordering;

public interface IOrderingRepository
{
    Task<OrderingSettingsRow> GetSettingsAsync(CancellationToken cancellationToken);
    Task SaveSettingsAsync(OrderingSettingsRow settings, string actorId, DateTime now, CancellationToken cancellationToken);
    Task SetNominationPauseAsync(DateTime? pausedUntil, string actorId, DateTime now, CancellationToken cancellationToken);
    Task<OrderSessionRow?> GetSessionByIdAsync(string id, CancellationToken cancellationToken);
    Task<OrderSessionRow?> GetSessionByGameIdAsync(string gameId, DateOnly businessDate, CancellationToken cancellationToken);
    Task<OrderSessionRow?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task CreateSessionAsync(OrderSessionRow session, string actorId, CancellationToken cancellationToken);
    Task RotateSessionCredentialsAsync(string sessionId, string tokenHash, string? recoveryCodeHash, DateTime now, CancellationToken cancellationToken);
    Task UpdateSessionAsync(string sessionId, string? customerName, int? maxNominatedStaff, int? remainingMealCredit,
        string actorId, string actorRole, DateTime now, CancellationToken cancellationToken);
    Task<MenuProductRow?> GetMenuProductAsync(string referenceId, string kind, CancellationToken cancellationToken);
    Task<StaffOfferRow?> GetStaffOfferAsync(string staffId, string serviceId, CancellationToken cancellationToken);
    Task<string?> GetStaffNameAsync(string staffId, CancellationToken cancellationToken);
    Task<bool> IsStaffBusyAsync(string staffId, DateTime startsAt, DateTime endsAt, CancellationToken cancellationToken);
    Task CreateOrderAsync(NewOrderAggregate order, CancellationToken cancellationToken);
    Task<OrderBundle> GetOrdersBySessionAsync(string sessionId, CancellationToken cancellationToken);
    Task<OrderBundle> GetOrderAsync(string orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminOrderSessionRow>> GetAdminSessionsAsync(DateOnly businessDate, string? search,
        CancellationToken cancellationToken);
    Task<int> ExpireWaitingOrdersAsync(DateTime cutoff, DateTime now, CancellationToken cancellationToken);
    Task<string> ConfirmNomineeAsync(string orderId, string staffId, string actorId, DateTime now,
        CancellationToken cancellationToken);
    Task RescheduleOrderAsync(string orderId, DateTime startsAt, string actorId, string actorRole, DateTime now,
        CancellationToken cancellationToken);
    Task UpdateOrderAsync(string orderId, string? customerNote, string? internalNote, string? status,
        string actorId, string actorRole, DateTime now, CancellationToken cancellationToken);
    Task UpdateOrderItemAsync(string orderId, string itemId, string? name, int? unitPrice, int? quantity,
        string actorId, string actorRole, DateTime now, CancellationToken cancellationToken);
    Task DeleteOrderItemAsync(string orderId, string itemId, string actorId, string actorRole, DateTime now,
        CancellationToken cancellationToken);
    Task CancelPendingOrderAsync(string orderId, string? reason, string actorId, string actorRole, DateTime now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<(DateOnly BusinessDate, int OrderCount, int GrossAmount, int MealCreditApplied,
        int NetAmount, int StaffTipAmount, int StoreTipAmount)>> GetReportAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken);
}
