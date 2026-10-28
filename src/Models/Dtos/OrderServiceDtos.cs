namespace ToBeClarify.Api.Models.Dtos;

public sealed record OrderServiceTargetDto(string Id, string Name, string StaffId, int Version,
    string Status, bool Skipped, int PurchasedMinutes, int RestMinutes,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, DateTimeOffset? BusyUntil);

public sealed record OrderServiceActionDto(string Action, bool Enabled, string? ReasonCode,
    string? Reason, IReadOnlyList<OrderServiceTargetDto> Targets,
    IReadOnlyList<FulfillmentConflictDto> Conflicts);

public sealed record OrderServiceEligibilityDto(string OrderId, int FlowVersion, string Snapshot,
    DateTimeOffset CheckedAt, IReadOnlyList<OrderServiceActionDto> Actions);

public sealed class OrderServiceCommandRequest
{
    public string OperationId { get; set; } = "";
    public string Snapshot { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Reason { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? CompatibilityPayload { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LegacyPayloadHash { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ExpectedUnitId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int? ExpectedUnitVersion { get; set; }
    // Custom backfill requires one explicit entry for every pending service.
    public List<OrderServiceActualTimeRequest> ActualTimes { get; set; } = [];
}

public sealed class OrderServiceActualTimeRequest
{
    public string Id { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool StillInProgress { get; set; }
}

public sealed record OrderServiceCommandResultDto(string OrderId, string OperationId,
    string OrderStatus, IReadOnlyList<string> AffectedIds);
