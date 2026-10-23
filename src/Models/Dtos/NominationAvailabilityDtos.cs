namespace ToBeClarify.Api.Models.Dtos;

// Only availability is public to order-token holders; never return customer/order identifiers.
public sealed record NominationWindowDto(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record StaffNominationAvailabilityDto(
    string StaffId, bool IsAccepting, DateTimeOffset? ShiftStartsAt, DateTimeOffset? ShiftEndsAt,
    int BufferMinutes, bool IsServing, bool IsOverdue, IReadOnlyList<NominationWindowDto> Windows);

public sealed record NominationAvailabilityDto(
    DateOnly BusinessDate, DateTimeOffset CheckedAt, DateTimeOffset? ProjectedCloseAt,
    bool NominationPaused, IReadOnlyList<StaffNominationAvailabilityDto> Staff);
