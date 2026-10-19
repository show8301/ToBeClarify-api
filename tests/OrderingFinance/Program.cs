using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Services.Ordering;

var equal = OrderingTipAllocationCalculator.Calculate(100, "equal",
[
    new SaveOrderingFinanceTipAllocationRequest("c", null),
    new SaveOrderingFinanceTipAllocationRequest("a", null),
    new SaveOrderingFinanceTipAllocationRequest("b", null)
]);
Equal(string.Join(',', equal.Select(x => $"{x.StaffId}:{x.Amount}")), "a:34,b:33,c:33", "equal split assigns remainder in stable staff order");

var designated = OrderingTipAllocationCalculator.Calculate(17, "designated",
    [new SaveOrderingFinanceTipAllocationRequest("staff", null)]);
Equal(designated.Single().Amount, 17L, "designated split assigns the full tip");

var custom = OrderingTipAllocationCalculator.Calculate(101, "custom",
[
    new SaveOrderingFinanceTipAllocationRequest("a", 65m),
    new SaveOrderingFinanceTipAllocationRequest("b", 35m)
]);
Equal(string.Join(',', custom.OrderBy(x => x.StaffId).Select(x => $"{x.StaffId}:{x.Amount}")), "a:66,b:35", "custom split allocates fractional remainder");

var oneGil = OrderingTipAllocationCalculator.Calculate(1, "custom",
[
    new SaveOrderingFinanceTipAllocationRequest("b", 50m),
    new SaveOrderingFinanceTipAllocationRequest("a", 50m)
]);
Equal(string.Join(',', oneGil.OrderBy(x => x.StaffId).Select(x => $"{x.StaffId}:{x.Amount}")), "a:1,b:0", "custom tie break is deterministic");

Throws<BusinessException>(() => OrderingTipAllocationCalculator.Calculate(100, "custom",
[
    new SaveOrderingFinanceTipAllocationRequest("a", 70m),
    new SaveOrderingFinanceTipAllocationRequest("b", 20m)
]), "custom percentages must total one hundred");

Console.WriteLine("Ordering finance tip allocation contracts passed.");

static void Equal<T>(T actual, T expected, string label)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
}

static void Throws<TException>(Action action, string label) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
}
