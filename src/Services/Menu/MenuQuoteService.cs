using System.Text.Json;
using Dapper;
using MySqlConnector;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Services.Menu;

public sealed record MenuOrderSnapshot(IReadOnlyList<MenuLineSnapshot> Lines,
    IReadOnlyList<MenuRuleSnapshot> Rules, int PrepaidMealCredit, int BaseNominationFee, int SegmentMinutes);

public sealed class MenuQuoteService(AppDbContext db, IAppClock clock)
{
    public static MenuLineSnapshot ResolveLine(MenuDto menu, MealOrderLineRequest line)
    {
        if (line.Quantity is < 1 or > 99) throw new BusinessException("份數必須為 1–99。", "MENU_QUANTITY_INVALID");
        if (line.Kind == "item")
        {
            var item = menu.Categories.SelectMany(x => x.Items).SingleOrDefault(x => x.Id == line.ReferenceId)
                ?? throw Unavailable();
            return new("item", item.Id, item.ItemName, item.Price, line.Quantity, item.Policy.EventCategories, []);
        }
        if (line.Kind != "set") throw Unavailable();
        var set = menu.Sets.SingleOrDefault(x => x.Id == line.ReferenceId);
        if (set is null || !set.IsOrderable) throw Unavailable();
        return new("set", set.Id, set.SetName, set.SetPrice, line.Quantity, set.EffectiveEventCategories,
            set.Items.Select(x => new MenuComponentSnapshot(x.MenuItemId, x.ItemName, x.ItemRole,
                checked(x.Quantity * line.Quantity), x.EventCategories)).ToArray());
    }
    private static BusinessException Unavailable() => new("餐點或套餐內容已停售，請更新菜單重新選擇。", "MENU_PRODUCT_UNAVAILABLE");
    public static string Fingerprint(NewOrderAggregate order) => MenuPolicies.Hash(new {
        Items = order.Items.Select(x => new { x.ItemType, x.ReferenceId, x.Name, x.UnitPrice, x.Quantity, x.SegmentCount, x.DurationMinutes, x.LineTotal, x.PriceRule }),
        Nominees = order.Nominees.Select(x => new { x.StaffId, x.ServiceId, x.StartsAt, x.ServiceEndsAt, x.BusyUntil }),
        Rooms = order.Rooms.Select(x => new { x.RoomId, x.StartsAt, x.EndsAt, x.UnitPrice }),
        Tips = order.Tips.Select(x => new { x.StaffId, x.Amount, x.StaffPercentage, x.StorePercentage }),
        order.Subtotal, order.MealCreditApplied, order.TotalAmount, order.StoreConfirmationStatus, order.MenuSnapshotJson
    });
    private static string RequestJson(SubmitOrderRequest request) => JsonSerializer.Serialize(new {
        request.Meals, request.Nominations, request.Rooms, request.Tips, request.CustomerNote
    }, MenuPolicies.Json);
    public async Task<MenuQuoteDto> CreateAsync(OrderSessionRow session, SubmitOrderRequest request, NewOrderAggregate order, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("D");
        var expires = clock.LocalDateTime.AddMinutes(5);
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO MENU_QUOTES (ID,SESSION_ID,REQUEST_JSON,SNAPSHOT_JSON,FINGERPRINT,EXPIRES_AT)
            VALUES (@Id,@SessionId,@Request,@Snapshot,@Fingerprint,@Expires);
            """, new { Id = id, SessionId = session.Id, Request = RequestJson(request), Snapshot = order.MenuSnapshotJson,
                Fingerprint = Fingerprint(order), Expires = expires }, cancellationToken: ct));
        var snapshot = JsonSerializer.Deserialize<MenuOrderSnapshot>(order.MenuSnapshotJson!, MenuPolicies.Json)!;
        return new(id, order.Subtotal, snapshot.Lines.Sum(x => checked(x.UnitPrice * x.Quantity)), order.MealCreditApplied,
            session.RemainingMealCredit - order.MealCreditApplied, order.TotalAmount, snapshot.Lines, snapshot.Rules,
            new DateTimeOffset(expires, TimeSpan.FromHours(8))) {
                Charges = order.Items.Select(x=>new OrderQuoteCharge(x.Name,x.Quantity,x.UnitPrice,x.LineTotal)).ToArray(),
                RequiresStoreConfirmation = order.StoreConfirmationStatus == "pending"
            };
    }
    public async Task<string?> ExistingOrderAsync(string sessionId, string? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT ORDER_ID FROM MENU_QUOTES WHERE ID=@Id AND SESSION_ID=@SessionId;", new { Id = id, SessionId = sessionId }, cancellationToken: ct));
    }
    public async Task ValidateAsync(string sessionId, SubmitOrderRequest request, NewOrderAggregate order, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.QuoteToken)) return;
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<QuoteRow>(new CommandDefinition(
            "SELECT SESSION_ID AS SessionId, REQUEST_JSON AS RequestJson, FINGERPRINT AS Fingerprint, EXPIRES_AT AS ExpiresAt FROM MENU_QUOTES WHERE ID=@Id;",
            new { Id = request.QuoteToken }, cancellationToken: ct));
        if (row is null || row.SessionId != sessionId || row.ExpiresAt <= clock.LocalDateTime || row.RequestJson != RequestJson(request) || row.Fingerprint != Fingerprint(order))
            throw Changed();
    }
    private static ConflictException Changed() => new("價格、消費規則或可用狀態已變更，請重新取得報價並確認。", "MENU_QUOTE_CHANGED");

    public static async Task<string?> ConsumeAsync(MySqlConnection connection, MySqlTransaction tx, NewOrderAggregate order, CancellationToken ct)
    {
        if (order.QuoteId is null) return null;
        var row = await connection.QuerySingleOrDefaultAsync<QuoteRow>(new CommandDefinition(
            "SELECT SESSION_ID AS SessionId, ORDER_ID AS OrderId, FINGERPRINT AS Fingerprint, EXPIRES_AT AS ExpiresAt FROM MENU_QUOTES WHERE ID=@Id FOR UPDATE;",
            new { Id = order.QuoteId }, tx, cancellationToken: ct));
        if (row is null || row.SessionId != order.SessionId) throw Changed();
        if (row.OrderId is not null) return row.OrderId;
        if (row.ExpiresAt <= order.SubmittedAt || row.Fingerprint != order.QuoteFingerprint) throw Changed();
        await connection.ExecuteAsync(new CommandDefinition("UPDATE MENU_QUOTES SET ORDER_ID=@OrderId WHERE ID=@Id;",
            new { OrderId = order.Id, Id = order.QuoteId }, tx, cancellationToken: ct));
        return null;
    }

    public static async Task ValidateInventoryAsync(MySqlConnection connection, MySqlTransaction tx, NewOrderAggregate order, CancellationToken ct)
    {
        if (order.MenuSnapshotJson is null) return;
        var snapshot = JsonSerializer.Deserialize<MenuOrderSnapshot>(order.MenuSnapshotJson, MenuPolicies.Json)!;
        // Lock complete menu tables in a fixed order, including gaps, so component additions cannot race submission.
        var categories = (await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            "SELECT ID AS Id, IS_ENABLED AS Enabled FROM MENU_CATEGORIES ORDER BY ID FOR UPDATE;", transaction: tx, cancellationToken: ct))).ToDictionary(x => x.Id);
        var items = (await connection.QueryAsync<ProductRow>(new CommandDefinition(
            "SELECT ID AS Id, CATEGORY_ID AS CategoryId, ITEM_NAME AS Name, PRICE AS Price, IS_AVAILABLE AS Available, POLICY_JSON AS PolicyJson FROM MENU_ITEMS ORDER BY ID FOR UPDATE;", transaction: tx, cancellationToken: ct))).ToDictionary(x => x.Id);
        var sets = (await connection.QueryAsync<ProductRow>(new CommandDefinition(
            "SELECT ID AS Id, SET_NAME AS Name, SET_PRICE AS Price, IS_AVAILABLE AS Available, POLICY_JSON AS PolicyJson FROM MENU_SETS ORDER BY ID FOR UPDATE;", transaction: tx, cancellationToken: ct))).ToDictionary(x => x.Id);
        var parts = (await connection.QueryAsync<PartRow>(new CommandDefinition(
            "SELECT SET_ID AS SetId, MENU_ITEM_ID AS ItemId, ITEM_ROLE AS Role, QUANTITY AS Quantity FROM MENU_SET_ITEMS ORDER BY SET_ID,SORT_ORDER,ID FOR UPDATE;", transaction: tx, cancellationToken: ct))).ToArray();
        foreach (var line in snapshot.Lines)
        {
            var source = line.Kind == "set" ? sets : items;
            if (!source.TryGetValue(line.ReferenceId, out var product) || !product.Available || product.Price != line.UnitPrice || product.Name != line.Name) throw Changed();
            var policy = MenuPolicies.Read<ProductPolicy>(product.PolicyJson);
            if (line.Kind == "item")
            {
                if (!policy.CanOrderAlone || !categories.TryGetValue(product.CategoryId!, out var category) || !category.Enabled || !policy.EventCategories.SequenceEqual(line.EventCategories)) throw Changed();
            }
            else
            {
                var current = parts.Where(x => x.SetId == line.ReferenceId).ToArray();
                if (current.Length == 0 || current.Length != line.Components.Count) throw Changed();
                var components = new List<MenuComponentSnapshot>();
                foreach (var part in current)
                {
                    if (!items.TryGetValue(part.ItemId, out var item) || !item.Available) throw Changed();
                    components.Add(new(part.ItemId, item.Name, part.Role, checked(part.Quantity * line.Quantity), MenuPolicies.Read<ProductPolicy>(item.PolicyJson).EventCategories));
                }
                if (MenuPolicies.Hash(components.OrderBy(x => x.MenuItemId)) != MenuPolicies.Hash(line.Components.OrderBy(x => x.MenuItemId)) ||
                    !policy.EventCategories.Concat(components.SelectMany(x => x.EventCategories)).Distinct().Order().SequenceEqual(line.EventCategories)) throw Changed();
            }
        }
        var settings = await connection.QuerySingleAsync<OrderingSettingsRow>(new CommandDefinition(
            "SELECT MINIMUM_MEAL_CREDIT AS MinimumMealCredit, BASE_NOMINATION_FEE AS BaseNominationFee, SEGMENT_MINUTES AS SegmentMinutes FROM ORDERING_SETTINGS WHERE ID='default' FOR UPDATE;", transaction: tx, cancellationToken: ct));
        if (settings.BaseNominationFee != snapshot.BaseNominationFee || settings.SegmentMinutes != snapshot.SegmentMinutes) throw Changed();
        var rules = await connection.QueryAsync<PricingRuleRow>(new CommandDefinition(
            "SELECT ID AS Id,TITLE AS Title,DESCRIPTION AS Description,PRICE_TEXT AS PriceText,POLICY_JSON AS PolicyJson FROM PRICING_RULES WHERE IS_ENABLED=TRUE ORDER BY SORT_ORDER,CREATED_AT FOR UPDATE;", transaction: tx, cancellationToken: ct));
        var now = new DateTimeOffset(order.SubmittedAt, TimeSpan.FromHours(8));
        var active = rules.Select(x => new { Row=x, Policy=MenuPolicies.Read<PricingPolicy>(x.PolicyJson) })
            .Where(x => x.Policy.ShowOnOrder && (!x.Policy.ValidFrom.HasValue || x.Policy.ValidFrom <= now) && (!x.Policy.ValidUntil.HasValue || x.Policy.ValidUntil > now))
            .Select(x => new MenuRuleSnapshot(x.Row.Id,x.Row.Title,x.Row.Description,x.Policy.Mode != "system" ? x.Row.PriceText : x.Policy.Source switch {
                "minimum_meal_credit" => $"{settings.MinimumMealCredit:N0} Gil／每次入場餐點信物",
                "base_nomination_fee" => $"{settings.BaseNominationFee:N0} Gil／每節 {settings.SegmentMinutes} 分鐘", _ => null },x.Policy)).ToArray();
        if (MenuPolicies.Hash(active) != MenuPolicies.Hash(snapshot.Rules)) throw Changed();
    }
    private sealed class QuoteRow { public string SessionId { get; set; } = ""; public string RequestJson { get; set; } = ""; public string Fingerprint { get; set; } = ""; public DateTime ExpiresAt { get; set; } public string? OrderId { get; set; } }
    private sealed class CategoryRow { public string Id { get; set; } = ""; public bool Enabled { get; set; } }
    private sealed class ProductRow { public string Id { get; set; } = ""; public string? CategoryId { get; set; } public string Name { get; set; } = ""; public int Price { get; set; } public bool Available { get; set; } public string? PolicyJson { get; set; } }
    private sealed class PartRow { public string SetId { get; set; } = ""; public string ItemId { get; set; } = ""; public string Role { get; set; } = ""; public int Quantity { get; set; } }
}
