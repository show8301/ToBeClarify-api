using Dapper;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Ordering;

public sealed partial class OrderingRepository
{
    public Task<OrderBundle> GetServiceOrdersAsync(DateOnly day, CancellationToken ct)
        => GetOrderBundleAsync("""
            (EXISTS (SELECT 1 FROM CUSTOMER_ORDER_SESSIONS S WHERE S.ID=O.SESSION_ID AND S.BUSINESS_DATE=@Value)
             OR EXISTS (SELECT 1 FROM ORDER_FULFILLMENT_UNITS F
                 JOIN BUSINESS_PERIODS P ON P.ID=COALESCE(F.FULFILLMENT_PERIOD_ID,F.BUSINESS_PERIOD_ID)
                 WHERE F.ORDER_ID=O.ID AND P.BUSINESS_DATE=@Value AND F.UNIT_STATUS NOT IN ('cancelled','carried_forward')))
            """, day.ToString("yyyy-MM-dd"), ct);

    public async Task<IReadOnlyList<AdminOrderSessionRow>> GetServiceSessionsAsync(DateOnly day, CancellationToken ct)
    {
        return await QueryAsync<AdminOrderSessionRow>($"""
            SELECT {SessionColumns},COUNT(O.ID) AS OrderCount,
                SUM(CASE WHEN O.ORDER_STATUS IN ('submitted','needs_reschedule','partially_confirmed') THEN 1 ELSE 0 END) AS WaitingOrderCount,
                SUM(CASE WHEN O.ORDER_STATUS IN ('confirmed','in_service','completed') THEN 1 ELSE 0 END) AS ConfirmedOrderCount,
                COALESCE(SUM(CASE WHEN O.ORDER_STATUS NOT IN ('cancelled','expired','rejected') THEN O.TOTAL_AMOUNT ELSE 0 END),0) AS TotalAmount,
                MAX(O.SUBMITTED_AT) AS LastOrderedAt
            FROM CUSTOMER_ORDER_SESSIONS S LEFT JOIN ORDERS O ON O.SESSION_ID=S.ID
            WHERE S.BUSINESS_DATE=@Day OR EXISTS (
                SELECT 1 FROM ORDER_FULFILLMENT_UNITS F JOIN ORDERS R ON R.ID=F.ORDER_ID
                JOIN BUSINESS_PERIODS P ON P.ID=COALESCE(F.FULFILLMENT_PERIOD_ID,F.BUSINESS_PERIOD_ID)
                WHERE R.SESSION_ID=S.ID AND P.BUSINESS_DATE=@Day AND F.UNIT_STATUS NOT IN ('cancelled','carried_forward'))
            GROUP BY S.ID ORDER BY LastOrderedAt DESC;
            """, new { Day = day.ToDateTime(TimeOnly.MinValue) }, ct);
    }

    public async Task<IReadOnlyList<OrderFulfillmentDto>> GetServiceFulfillmentAsync(DateOnly day, CancellationToken ct)
    {
        await using var c = await DbContext.CreateOpenConnectionAsync(ct);
        var ids = await c.QueryAsync<string>(new CommandDefinition("""
            SELECT DISTINCT O.ID FROM ORDERS O JOIN ORDER_FULFILLMENT_UNITS F ON F.ORDER_ID=O.ID
            JOIN BUSINESS_PERIODS P ON P.ID=COALESCE(F.FULFILLMENT_PERIOD_ID,F.BUSINESS_PERIOD_ID)
            WHERE O.FLOW_VERSION>=2 AND P.BUSINESS_DATE=@Day ORDER BY O.ID;
            """, new { Day = day.ToDateTime(TimeOnly.MinValue) }, cancellationToken: ct));
        var result = new List<OrderFulfillmentDto>();
        foreach (var id in ids) result.Add(await ReadFulfillmentAsync(c, null, id, ct));
        return result;
    }
}
