using Dapper;
using MySqlConnector;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public sealed partial class SettlementRepository
{
    public async Task<int> GetPendingOrderBackfillCountAsync(string settlementId, CancellationToken ct)
    {
        await using var connection = await DbContext.CreateOpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM ORDER_BACKFILLS B
            JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=B.SESSION_ID
            JOIN SETTLEMENT_RUNS R ON R.BUSINESS_DATE=S.BUSINESS_DATE
            WHERE R.ID=@SettlementId AND R.STATUS='finalized'
              AND B.APPLIED_SETTLEMENT_ID IS NULL;
            """, new { SettlementId = settlementId }, cancellationToken: ct));
    }

    private static Task MarkOrderBackfillsAppliedAsync(MySqlConnection connection, MySqlTransaction tx,
        string settlementId, DateTime businessDate, CancellationToken ct)
        => connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ORDER_BACKFILLS B JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=B.SESSION_ID
            SET B.APPLIED_SETTLEMENT_ID=@SettlementId
            WHERE S.BUSINESS_DATE=@Date AND B.APPLIED_SETTLEMENT_ID IS NULL;
            """, new { SettlementId = settlementId, Date = businessDate }, tx, cancellationToken: ct));
}
