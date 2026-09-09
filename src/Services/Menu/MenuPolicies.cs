using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Menu;

public static class MenuPolicies
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static T Read<T>(string? json)
        => JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(json) ? (typeof(T).IsArray ? "[]" : "{}") : json, Json)!;
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    public static void Validate(PricingPolicy? policy)
    {
        if (policy is null) return;
        if (policy.Mode is not ("information" or "system") ||
            (policy.Mode == "system" && policy.Source is not ("minimum_meal_credit" or "base_nomination_fee")) ||
            (policy.Mode == "information" && policy.Source is not null))
            throw new BusinessException("請選擇有效的規則模式與系統來源。", "PRICING_POLICY_INVALID");
        if (policy.ValidFrom.HasValue && policy.ValidUntil.HasValue && policy.ValidUntil <= policy.ValidFrom)
            throw new BusinessException("結束時間必須晚於生效時間。", "PRICING_PERIOD_INVALID");
    }
    public static void Validate(ProductPolicy? policy)
    {
        if (policy is null) return;
        if (policy.EventCategories is null || policy.EventCategories.Length > 16 ||
            policy.EventCategories.Distinct(StringComparer.Ordinal).Count() != policy.EventCategories.Length ||
            policy.EventCategories.Any(x => x != "champagne_tower"))
            throw new BusinessException("事件分類無效，請從可用分類中選擇。", "MENU_EVENT_CATEGORY_INVALID");
    }
    public static void ValidateTags(JsonElement? tags)
    {
        if (tags is null || tags.Value.ValueKind == JsonValueKind.Null) return;
        if (tags.Value.ValueKind != JsonValueKind.Array || tags.Value.GetArrayLength() > 12 ||
            tags.Value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(x.GetString()) || x.GetString()!.Length > 24))
            throw new BusinessException("標籤最多 12 個，每個 1–24 字。", "MENU_TAGS_INVALID");
    }
    public static void ValidateSet(SaveMenuSetRequest request, AdminMenuDto menu)
    {
        if (request.Items is null || request.Items.Count > 100 || (request.IsAvailable && request.Items.Count == 0))
            throw new BusinessException("供應中的套餐必須有內容，最多 100 列。", "MENU_SET_EMPTY");
        var ids = menu.Categories.SelectMany(x => x.Items).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (request.Items.Any(x => !ids.Contains(x.MenuItemId) || x.Quantity is < 1 or > 99 || x.ItemRole is not ("main" or "dessert" or "drink" or "other")))
            throw new BusinessException("套餐含無效餐點、角色或份數（1–99）。", "MENU_SET_ITEM_INVALID");
        if (request.Items.GroupBy(x => x.MenuItemId).Any(x => x.Count() > 1))
            throw new BusinessException("相同餐點請合併調整份數。", "MENU_SET_ITEM_DUPLICATED");
    }
    public static string SortRevision(AdminMenuDto menu) => SortHash(
        menu.PricingRules.Select(x => new SortRow("PRICING_RULES", x.Id, x.SortOrder, ""))
        .Concat(menu.Categories.Select(x => new SortRow("MENU_CATEGORIES", x.Id, x.SortOrder, "")))
        .Concat(menu.Categories.SelectMany(x => x.Items).Select(x => new SortRow("MENU_ITEMS", x.Id, x.SortOrder, x.CategoryId)))
        .Concat(menu.Sets.Select(x => new SortRow("MENU_SETS", x.Id, x.SortOrder, ""))));
    public static string SortHash(IEnumerable<SortRow> rows) => Hash(rows.OrderBy(x => x.Table, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal));
    public sealed record SortRow(string Table, string Id, int SortOrder, string CategoryId);
}

public sealed class MenuSortService(AppDbContext db, IAppClock clock)
{
    public async Task SortAsync(MenuSortRequest request, string actorId, CancellationToken ct)
    {
        if (request.Items is null || request.Categories is null || request.Sets is null || request.PricingRules is null)
            throw new BusinessException("排序資料不可為空。", "MENU_SORT_INVALID");
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var rows = new List<MenuPolicies.SortRow>();
        // Fixed table whitelist and order; lock the complete inventory before validating or writing.
        foreach (var table in new[] { "MENU_CATEGORIES", "MENU_ITEMS", "MENU_SETS", "PRICING_RULES" })
        {
            var category = table == "MENU_ITEMS" ? "`CATEGORY_ID`" : "''";
            rows.AddRange(await connection.QueryAsync<MenuPolicies.SortRow>(new CommandDefinition(
                $"SELECT '{table}' AS `Table`, ID AS Id, SORT_ORDER AS SortOrder, {category} AS CategoryId FROM `{table}` ORDER BY ID FOR UPDATE;", transaction: tx, cancellationToken: ct)));
        }
        if (MenuPolicies.SortHash(rows) != request.ExpectedRevision)
            throw new ConflictException("菜單排序或項目已變更，請重新載入後再排序。", "MENU_REVISION_CONFLICT");
        var groups = new List<(string Table, string CategoryId, string[] Ids)> {
            ("PRICING_RULES", "", request.PricingRules), ("MENU_CATEGORIES", "", request.Categories), ("MENU_SETS", "", request.Sets) };
        groups.AddRange(request.Items.Select(x => ("MENU_ITEMS", x.Key, x.Value)));
        if (!rows.Where(x => x.Table == "MENU_CATEGORIES").Select(x => x.Id).ToHashSet().SetEquals(request.Items.Keys))
            throw new BusinessException("排序必須包含所有分類。", "MENU_SORT_INVALID");
        foreach (var group in groups)
        {
            var expected = rows.Where(x => x.Table == group.Table && x.CategoryId == group.CategoryId).Select(x => x.Id).ToHashSet();
            if (group.Ids is null || group.Ids.Length != expected.Count || !expected.SetEquals(group.Ids))
                throw new BusinessException("排序必須包含每個項目且不得重複。", "MENU_SORT_INVALID");
            for (var i = 0; i < group.Ids.Length; i++)
                await connection.ExecuteAsync(new CommandDefinition($"UPDATE `{group.Table}` SET SORT_ORDER=@Sort, UPDATED_AT=@Now, UPDATED_BY=@Actor WHERE ID=@Id;",
                    new { Sort = i, Now = clock.LocalDateTime, Actor = actorId, Id = group.Ids[i] }, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
    }
}
