using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Repositories.Shared;

namespace ToBeClarify.Api.Repositories.Admin.Settlement;

public static class SettlementPayrollValidation
{
    public static async Task ValidateAsync(MySqlConnection connection, MySqlTransaction tx, DateTime date,
        IEnumerable<(string StaffId, string Role, int? Version)> inputs, CancellationToken ct)
    {
        foreach (var group in inputs.GroupBy(x => x.StaffId).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var snapshot = await PayrollSnapshotStore.ReadAsync(connection, tx, group.Key, date, ct);
            Validate(snapshot, group);
        }
    }

    public static void Validate(PayrollSnapshotRow? snapshot, IEnumerable<(string StaffId, string Role, int? Version)> inputs)
    {
        var rows = inputs.ToArray();
        if (snapshot?.PayrollRole is null)
            throw new BusinessException("請先在出勤區確認上班計薪職位。", "PAYROLL_ROLE_UNRESOLVED");
        if (rows.Any(x => x.StaffId != snapshot.StaffId || x.Version != snapshot.Version))
            throw new ConflictException("計薪職位快照已變更，請刷新並核對輸入。", "PAYROLL_SNAPSHOT_CHANGED");
        if (rows.Count(x => x.Role == snapshot.PayrollRole) != 1
            || rows.Any(x => x.Role != snapshot.PayrollRole && !(x.Role == "backstage" && snapshot.CanUseBackstage))
            || rows.GroupBy(x => x.Role).Any(x => x.Count() > 1))
            throw new BusinessException("每人須有一份符合上班職位的主要計薪輸入；幕後兼任僅限開發人員。", "PAYROLL_INPUT_CONFLICT");
        if (rows.Any(x => x.Role == "backstage") && !snapshot.CanUseBackstage)
            throw new BusinessException("幕後工作僅限開發人員。", "BACKSTAGE_NOT_ALLOWED");
    }

    public static async Task ValidateStoredAsync(MySqlConnection connection, MySqlTransaction tx,
        string settlementId, DateTime date, CancellationToken ct)
    {
        var stored = await connection.QueryAsync<Input>(new CommandDefinition("""
            SELECT STAFF_ID AS StaffId,ROLE, PAYROLL_SNAPSHOT_VERSION AS Version
            FROM SETTLEMENT_STAFF_INPUTS WHERE SETTLEMENT_ID=@SettlementId;
            """, new { SettlementId = settlementId }, tx, cancellationToken: ct));
        await ValidateAsync(connection, tx, date, stored.Select(x => (x.StaffId, x.Role, x.Version)), ct);
    }

    private sealed class Input
    {
        public string StaffId { get; set; } = "";
        public string Role { get; set; } = "";
        public int? Version { get; set; }
    }
}
