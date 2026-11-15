using System.Globalization;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Repositories.Client.Calendar;
using ToBeClarify.Api.Services.Media;

namespace ToBeClarify.Api.Services.Client.Calendar;

public sealed record PublicCalendarShift(string StaffId, string DisplayName, string? AvatarUrl,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record PublicCalendarDay(string BusinessDate, string Status, DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt, IReadOnlyList<PublicCalendarShift> Staff);
public sealed record PublicBusinessCalendar(string Month, string Today, IReadOnlyList<PublicCalendarDay> Days);

public sealed class BusinessCalendarService(BusinessCalendarRepository repository, MediaUrlService media, IAppClock clock)
{
    public async Task<PublicBusinessCalendar> GetAsync(string? month, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.LocalDateTime);
        month ??= today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var first) || first.Year < 1900 || first.Year > 9998)
            throw new BusinessException("請提供有效的月份（yyyy-MM）。", "CALENDAR_MONTH_INVALID");
        var last = first.AddMonths(1).AddDays(-1);
        var (business, shifts) = await repository.GetAsync(first, last, ct);
        var businessByDate = business.ToDictionary(row => DateOnly.FromDateTime(row.BusinessDate));
        var shiftsByDate = shifts.ToLookup(row => DateOnly.FromDateTime(row.BusinessDate));
        var days = new List<PublicCalendarDay>();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            businessByDate.TryGetValue(date, out var plan);
            var status = plan is null ? "unannounced" : plan.PeriodStatus is null ? "planned"
                : plan.PeriodStatus == "open" ? "open" : "operated";
            days.Add(new(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), status,
                plan is null ? null : Offset(plan.StartsAt), plan is null ? null : Offset(plan.EndsAt),
                shiftsByDate[date].Select(row => new PublicCalendarShift(row.StaffId, row.DisplayName,
                    media.BuildUrl(row.AvatarMediaId, "card"), Offset(row.StartsAt), Offset(row.EndsAt))).ToArray()));
        }
        return new(month, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), days);
    }

    private static DateTimeOffset Offset(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.FromHours(8));
}
