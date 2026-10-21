using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ToBeClarify.Api.Auth;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Infrastructure;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Repositories.Admin.Settlement;
using ToBeClarify.Api.Services.Admin.Settlement;

public class RuleRepositoryProbe : DispatchProxy
{
    public SettlementRuleRow Original = new() { Id = "settlement-default-normal", DayType = "normal", EffectiveFrom = new(2000, 1, 1), TimeRoundMinutes = 30 };
    public SettlementRuleRow NewRule = new() { Id = "new-rule", DayType = "normal", EffectiveFrom = new(2026, 10, 21), TimeRoundMinutes = 30, ServiceManagerHourlyRate = 60000 };
    public SettlementRunRow Run = new() { Id = "run", BusinessDate = new(2026, 10, 18), SessionNo = 1, DayType = "normal", Status = "draft", RuleVersionId = "settlement-default-normal" };
    public SaveInputsData? Saved;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "GetRunAsync" => Task.FromResult<SettlementRunRow?>(Run),
        "StaffMembersExistAsync" => Task.FromResult(true),
        "GetRulesAsync" => Task.FromResult<IReadOnlyList<SettlementRuleRow>>([Original, NewRule]),
        "GetEffectiveRuleAsync" => Task.FromResult<SettlementRuleRow?>(NewRule),
        "GetSourceDataAsync" => Task.FromResult(new SettlementSourceData()),
        "SaveInputsAsync" => Save((SaveInputsData)args![1]!),
        _ => throw new Exception("Unexpected repository call: " + method.Name)
    };
    private Task Save(SaveInputsData input)
    {
        Saved = input; Run.RuleVersionId = input.RuleVersionId; Run.RuleSnapshotJson = input.RuleSnapshotJson;
        Run.DayType = input.DayType!; return Task.CompletedTask;
    }
}

static class RuleSelectionChecks
{
    public static async Task Run()
    {
        var repository = DispatchProxy.Create<ISettlementRepository, RuleRepositoryProbe>();
        var state = (RuleRepositoryProbe)(object)repository;
        var service = new SettlementService(repository, new Clock(), null!, null!);
        var actor = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AdminAuthConstants.UserIdClaimType, "manager")]));
        var before = await service.GetOverviewAsync(new(2026, 10, 18), 1, default);
        Check(before.Rule.Id == state.Original.Id, "draft respects its persisted default rule id even when a newer rule exists");
        var after = await service.SaveInputsAsync(new() { BusinessDate = new(2026, 10, 18), RuleVersionId = state.NewRule.Id,
            DayType = "normal", ExpectedInputVersion = 0 }, actor, default);
        Check(after.Rule.Id == state.NewRule.Id && state.Saved!.RuleVersionId == state.NewRule.Id
            && JsonSerializer.Deserialize<SettlementRuleRow>(state.Saved.RuleSnapshotJson!)!.ServiceManagerHourlyRate == 60000,
            "selecting a stored rule saves both its id and immutable configuration snapshot");
        Check((await service.GetOverviewAsync(new(2026, 10, 18), 1, default)).Rule.Id == state.NewRule.Id, "selected rule survives reload");
        await Reject(() => service.SaveInputsAsync(new() { BusinessDate = new(2026, 10, 18), RuleVersionId = "missing", DayType = "normal" }, actor, default));
        await Reject(() => service.SaveInputsAsync(new() { BusinessDate = new(2026, 10, 18), RuleVersionId = state.NewRule.Id, DayType = "event" }, actor, default));
        state.Run.Status = "finalized";
        await Reject(() => service.SaveInputsAsync(new() { BusinessDate = new(2026, 10, 18), RuleVersionId = state.Original.Id, DayType = "normal" }, actor, default));
        Check(state.Run.RuleVersionId == state.NewRule.Id, "finalized rules cannot be replaced through draft input save");
        Console.WriteLine("Rule selection persistence, legacy id and validation checks passed.");
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static async Task Reject(Func<Task<SettlementOverviewDto>> operation)
    {
        try { await operation(); } catch (Exception e) when (e is BusinessException or ConflictException) { return; }
        throw new Exception("Expected selection rejection");
    }
    private sealed class Clock : IAppClock
    {
        public DateTimeOffset Now => new(2026, 10, 21, 12, 0, 0, TimeSpan.FromHours(8));
        public DateTime LocalDateTime => Now.DateTime;
    }
}
