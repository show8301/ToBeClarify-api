using ToBeClarify.Api.Models.Dtos;

namespace ToBeClarify.Api.Services.Ordering;

public sealed partial class OrderingService
{
    public async Task<NominationAvailabilityDto> GetNominationAvailabilityAsync(string token, CancellationToken ct)
    {
        var session = await ValidateTokenAsync(token, ct);
        var context = await ResolveSessionBusinessContextAsync(session, ct);
        var settings = await _repository.GetSettingsAsync(ct);
        var now = _clock.LocalDateTime;
        var day = DateOnly.FromDateTime(session.BusinessDate);
        // Closing coordination remains possible, but never outside an approved shift
        // or the existing 48-hour business-period limit.
        var horizon = (context.ActualOpenedAt?.DateTime ?? context.ReferenceStartsAt.DateTime).AddHours(48);
        var paused = settings.NominationPausedUntil > now || session.SessionStatus != "active"
            || (!context.CanCustomerSubmit && !context.IsTestOverride);
        var staff = paused ? [] : await _repository.GetNominationAvailabilityAsync(day, now, horizon, ct);
        return new(day, new DateTimeOffset(now, TimeSpan.FromHours(8)), context.ProjectedCloseAt, paused, staff);
    }
}
