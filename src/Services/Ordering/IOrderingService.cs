using System.Security.Claims;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Ordering;

public interface IOrderingService
{
    Task<OrderSessionIssuedDto> CreateSessionAsync(CreateOrderSessionRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderSessionIssuedDto> RotateSessionCredentialsAsync(string sessionId, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderSessionDto> UpdateSessionAsync(string sessionId, UpdateOrderSessionRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderSessionAccessDto> AccessSessionAsync(string token, CancellationToken cancellationToken);
    Task<OrderSessionIssuedDto> RecoverSessionAsync(RecoverOrderSessionRequest request, CancellationToken cancellationToken);
    Task<OrderCatalogDto> GetCatalogAsync(string token, CancellationToken cancellationToken);
    Task<OrderDto> SubmitOrderAsync(string token, SubmitOrderRequest request, CancellationToken cancellationToken);
    Task<OrderDto> SubmitAddonAsync(string token, SubmitAddonRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string token, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminOrderSessionDto>> GetAdminSessionsAsync(DateOnly? businessDate, string? search, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDto>> GetAdminOrdersAsync(string sessionId, CancellationToken cancellationToken);
    Task<OrderingBusinessContextDto> GetBusinessContextAsync(CancellationToken cancellationToken);
    Task<OrderingBusinessDayOverrideDto?> GetBusinessDayOverrideAsync(CancellationToken cancellationToken);
    Task<OrderingBusinessDayOverrideDto> SaveBusinessDayOverrideAsync(
        UpdateOrderingBusinessDayOverrideRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task DisableBusinessDayOverrideAsync(ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken);
    Task<OrderingSettingsDto> SaveSettingsAsync(UpdateOrderingSettingsRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderingSettingsDto> PauseNominationAsync(PauseNominationRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> ConfirmNomineeAsync(string orderId, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<IReadOnlyList<StaffServiceDto>> GetAddonOptionsAsync(string nomineeId, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> SubmitAdminAddonAsync(string nomineeId, SubmitAddonRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> ConfirmAddonAsync(string orderId, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> RescheduleOrderAsync(string orderId, RescheduleOrderRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> BackfillServedOrderAsync(string orderId, BackfillServedOrderRequest request,
        ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> ShortenNominationAsync(string orderId, string nomineeId, ShortenNominationRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> UpdateOrderAsync(string orderId, UpdateAdminOrderRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> TransitionOrderAsync(string orderId, OrderTransitionRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> UpdateOrderItemAsync(string orderId, string itemId, UpdateOrderItemRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<OrderDto> DeleteOrderItemAsync(string orderId, string itemId, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task CancelOrderAsync(string orderId, OrderActionRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderingReportSummaryDto>> GetReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<int> ExpireWaitingOrdersAsync(CancellationToken cancellationToken);
}
