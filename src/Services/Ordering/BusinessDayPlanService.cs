using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Repositories.Ordering;

namespace ToBeClarify.Api.Services.Ordering;

public sealed record BusinessDayPlanDto(DateOnly BusinessDate, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, int Version, bool IsSaved, bool IsOpened, int FlowVersion);

public sealed class SaveBusinessDayPlanRequest
{
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    [Range(0,int.MaxValue)] public int ExpectedVersion { get; init; }
    [Required,StringLength(80,MinimumLength=8)] public string OperationId { get; init; } = "";
    [StringLength(500)] public string? Reason { get; init; }
}

public sealed class BusinessDayPlanService(AppDbContext db, IOrderingRepository repository, IAppClock clock)
{
    public async Task<BusinessDayPlanDto> GetAsync(DateOnly date, CancellationToken ct)
    {
        await using var connection = await db.CreateOpenConnectionAsync(ct);
        var plan = await connection.QuerySingleOrDefaultAsync<PlanRow>(new CommandDefinition(
            "SELECT STARTS_AT StartsAt, ENDS_AT EndsAt, VERSION Version FROM BUSINESS_DAY_PLANS WHERE BUSINESS_DATE=@Day",
            new {Day=date.ToDateTime(TimeOnly.MinValue)}, cancellationToken:ct));
        var period = await repository.GetBusinessPeriodByDateAsync(date, ct);
        var settings = await repository.GetSettingsAsync(ct);
        var starts = plan?.StartsAt ?? period?.StartsAt ?? date.ToDateTime(TimeOnly.MinValue).AddMinutes(settings.BusinessDayStartMinute);
        var ends = plan?.EndsAt ?? period?.EndsAt ?? date.AddDays(settings.BusinessDayEndsNextDay?1:0).ToDateTime(TimeOnly.MinValue).AddMinutes(settings.BusinessDayEndMinute);
        return new(date, Offset(starts), Offset(ends), plan?.Version??0, plan is not null, period is not null, period?.FlowVersion??1);
    }

    public async Task<BusinessDayPlanDto> SaveAsync(DateOnly date, SaveBusinessDayPlanRequest request, string actor, CancellationToken ct)
    {
        var starts=request.StartsAt.ToOffset(TimeSpan.FromHours(8)).DateTime;
        var ends=request.EndsAt.ToOffset(TimeSpan.FromHours(8)).DateTime;
        if(DateOnly.FromDateTime(starts)!=date || ends<=starts || ends>starts.AddHours(48))
            throw new BusinessException("開始日期必須為營業日；結束須晚於開始且不超過 48 小時。", "BUSINESS_PLAN_TIME_INVALID");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{date,request,actor}))));
        await using var connection=await db.CreateOpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        await connection.ExecuteScalarAsync<string>(new CommandDefinition("SELECT ID FROM ORDERING_RUNTIME_LOCKS WHERE ID='operating_period' FOR UPDATE",transaction:tx,cancellationToken:ct));
        var prior=await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT PAYLOAD_HASH FROM BUSINESS_PERIOD_OPERATIONS WHERE OPERATION_ID=@OperationId",request,tx,cancellationToken:ct));
        if(prior is not null)
        {
            if(prior!=hash) throw new ConflictException("操作編號已使用於不同內容。", "OPERATION_ID_CONFLICT");
            await tx.CommitAsync(ct); return await GetAsync(date,ct);
        }
        var day=date.ToDateTime(TimeOnly.MinValue);
        if(await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM BUSINESS_PERIODS WHERE BUSINESS_DATE=@Day",new{Day=day},tx,cancellationToken:ct))>0)
            throw new BusinessException("已開店的原始計畫須保留；請使用調整預計關店時間。", "BUSINESS_PLAN_ALREADY_OPENED");
        var version=await connection.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT VERSION FROM BUSINESS_DAY_PLANS WHERE BUSINESS_DATE=@Day FOR UPDATE",new{Day=day},tx,cancellationToken:ct))??0;
        if(version!=request.ExpectedVersion) throw new ConflictException("營業計畫已更新，請重新整理。", "BUSINESS_PLAN_VERSION_CONFLICT");
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO BUSINESS_DAY_PLANS(BUSINESS_DATE,STARTS_AT,ENDS_AT,VERSION,UPDATED_BY,UPDATED_AT,REASON)
            VALUES(@Day,@Starts,@Ends,1,@Actor,@Now,@Reason)
            ON DUPLICATE KEY UPDATE STARTS_AT=@Starts,ENDS_AT=@Ends,VERSION=VERSION+1,UPDATED_BY=@Actor,UPDATED_AT=@Now,REASON=@Reason
            """,new{Day=day,Starts=starts,Ends=ends,Actor=actor,Now=clock.LocalDateTime,request.Reason},tx,cancellationToken:ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO BUSINESS_PERIOD_OPERATIONS(OPERATION_ID,PAYLOAD_HASH,BUSINESS_DATE,ACTION,ACTOR_ID,CREATED_AT)
            VALUES(@OperationId,@Hash,@Day,'save_plan',@Actor,@Now)
            """,new{request.OperationId,Hash=hash,Day=day,Actor=actor,Now=clock.LocalDateTime},tx,cancellationToken:ct));
        await tx.CommitAsync(ct); return await GetAsync(date,ct);
    }
    private static DateTimeOffset Offset(DateTime date)=>new(DateTime.SpecifyKind(date,DateTimeKind.Unspecified),TimeSpan.FromHours(8));
    private sealed class PlanRow { public DateTime StartsAt{get;set;} public DateTime EndsAt{get;set;} public int Version{get;set;} }
}
