using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Repositories.Ordering;

namespace ToBeClarify.Api.Services.Ordering;

public sealed record BusinessDayContext
{
    public DateOnly BusinessDate { get; init; }
    public string? BusinessPeriodId { get; init; }
    public int FlowVersion { get; init; } = 1;
    public string PeriodStatus { get; init; } = "scheduled";
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public bool IsTestOverride { get; init; }
}

public interface IBusinessDayContext
{
    Task<BusinessDayContext> GetCurrentAsync(CancellationToken cancellationToken);
    Task<BusinessDayContext> GetForDateAsync(DateOnly businessDate, CancellationToken cancellationToken);
}

public sealed class BusinessDayContextService(IOrderingRepository repository, IAppClock clock,
    BusinessDayPlanService plans) : IBusinessDayContext
{
    public async Task<BusinessDayContext> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var now = clock.LocalDateTime;
        var activeOverride = await repository.GetActiveBusinessDayOverrideAsync(now, cancellationToken);
        if (activeOverride is not null)
        {
            var day = await GetForDateAsync(DateOnly.FromDateTime(activeOverride.BusinessDate), cancellationToken);
            return day with { StartsAt=activeOverride.StartsAt, EndsAt=activeOverride.EndsAt,
                PeriodStatus="open", IsTestOverride=true };
        }
        var active = await repository.GetActiveBusinessPeriodAsync(now, cancellationToken);
        if (active?.PeriodStatus == "open")
            return await GetForDateAsync(DateOnly.FromDateTime(active.BusinessDate), cancellationToken);
        var today = DateOnly.FromDateTime(now);
        var yesterday = await GetForDateAsync(today.AddDays(-1), cancellationToken);
        return now >= yesterday.StartsAt && now < yesterday.EndsAt
            ? yesterday : await GetForDateAsync(today, cancellationToken);
    }

    public async Task<BusinessDayContext> GetForDateAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        var period = await repository.GetBusinessPeriodByDateAsync(businessDate, cancellationToken);
        if (period is not null) return new() { BusinessDate=businessDate, BusinessPeriodId=period.Id,
            FlowVersion=period.FlowVersion, PeriodStatus=period.PeriodStatus,
            StartsAt=period.StartsAt, EndsAt=period.ProjectedCloseAt ?? period.EndsAt };
        var plan = await plans.GetAsync(businessDate, cancellationToken);
        return new() { BusinessDate=businessDate, StartsAt=plan.StartsAt.DateTime, EndsAt=plan.EndsAt.DateTime };
    }
}
