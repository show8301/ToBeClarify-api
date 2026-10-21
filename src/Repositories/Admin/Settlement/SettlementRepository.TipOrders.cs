using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public sealed partial class SettlementRepository
{
    private static async Task AddDistributedOrderTipsAsync(MySqlConnection connection, MySqlTransaction? transaction,
        DateOnly businessDate, List<SettlementTipRevenueRow> tips, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<DistributedTipRow>(new CommandDefinition("""
            SELECT CAST(O.ID AS CHAR(36)) AS OrderId,T.TIP_AMOUNT AS Amount,D.SNAPSHOT_JSON AS SnapshotJson
            FROM ORDER_TIP_DISTRIBUTIONS D JOIN ORDER_TIPS T ON T.ORDER_ITEM_ID=D.ORDER_ITEM_ID
            JOIN ORDERS O ON O.ID=T.ORDER_ID JOIN CUSTOMER_ORDER_SESSIONS S ON S.ID=O.SESSION_ID
            WHERE S.BUSINESS_DATE=@Date AND O.ORDER_STATUS NOT IN ('cancelled','expired','rejected');
            """, new { Date = businessDate.ToDateTime(TimeOnly.MinValue) }, transaction, cancellationToken: ct));
        foreach (var row in rows)
        {
            var snapshot = JsonSerializer.Deserialize<OrderTipDistributionSnapshot>(row.SnapshotJson)!;
            if (snapshot.Mode == "public")
            {
                tips.Add(new SettlementTipRevenueRow { OrderId = row.OrderId, TipAmount = row.Amount,
                    SourceType = "public_tip", SourceLabel = "公共小費（按工時分配）" });
                continue;
            }
            if (snapshot.PublicAmount > 0)
                tips.Add(new SettlementTipRevenueRow { OrderId = row.OrderId, TipAmount = snapshot.PublicAmount,
                    SourceType = "public_tip", SourceLabel = "公共小費（按工時分配）" });
            foreach (var share in snapshot.Allocations)
                tips.Add(new SettlementTipRevenueRow { OrderId = row.OrderId, StaffId = share.StaffId,
                    StaffAmount = share.Amount, TipAmount = share.Amount, StoreAmount = 0, BasisAmount = row.Amount,
                    Percentage = share.Percentage ?? 0, SourceType = snapshot.Mode == "equal" ? "legacy_equal" : "order_tip",
                    SourceLabel = snapshot.Mode == "equal" ? "訂單小費（均分）" : snapshot.Mode == "custom" ? "訂單小費（自訂比例）" : "訂單小費（指定）" });
        }
    }
    private sealed class DistributedTipRow
    {
        public string OrderId { get; set; } = "";
        public int Amount { get; set; }
        public string SnapshotJson { get; set; } = "";
    }
}
