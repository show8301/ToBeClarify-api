using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Models.Entities;
using ToBeClarify.Api.Services.Ordering;

var date = new DateTime(2026, 10, 18, 21, 0, 0);
var period = new BusinessPeriodRow { Id = "period", StartsAt = date, ActualOpenedAt = date,
    ActualClosedAt = date.AddHours(8), PeriodStatus = "closed" };
var session = new OrderSessionRow { Id = "session", BusinessPeriodId = period.Id, SessionStatus = "readonly" };
var backfill = new OrderBackfillRequest { OperationId = Guid.NewGuid().ToString("D"), Reason = "missed sale",
    OccurredAt = new DateTimeOffset(date.AddHours(1), TimeSpan.FromHours(8)) };
foreach (var state in new[] { "open", "closed", "settled" })
{
    period.PeriodStatus = state;
    OrderBackfillPolicy.Validate(backfill, session, period, date.AddDays(2));
}
Throws(() => OrderBackfillPolicy.Validate(backfill with { Reason = " " }, session, period, date.AddDays(2)));
Throws(() => OrderBackfillPolicy.Validate(backfill with { OccurredAt = backfill.OccurredAt.AddDays(1) }, session, period, date.AddDays(2)));
Throws(() => OrderBackfillPolicy.Validate(backfill, session, period, date));
session.SessionStatus = "disabled";
Throws(() => OrderBackfillPolicy.Validate(backfill, session, period, date.AddDays(2)));
session.SessionStatus = "readonly";
var first = new SubmitOrderRequest { Backfill = backfill, Meals = [new() { ReferenceId = "meal", Quantity = 2 }] };
var reordered = new SubmitOrderRequest { Backfill = backfill with { OperationId = Guid.NewGuid().ToString("D"), Reason = "another clerk",
    OccurredAt = backfill.OccurredAt.ToUniversalTime() }, Meals = [new() { ReferenceId = "meal", Quantity = 1, UnitPrice = 100 }, new() { ReferenceId = "meal", Quantity = 1 }] };
Equal(OrderBackfillPolicy.DuplicateHash(first), OrderBackfillPolicy.DuplicateHash(reordered), "semantic duplicate ignores clerk, reason, price defaults and item splitting");
Check(OrderBackfillPolicy.PayloadHash(first) != OrderBackfillPolicy.PayloadHash(reordered), "different payload must conflict on a reused operation ID");
Check(OrderBackfillPolicy.DuplicateHash(first) != OrderBackfillPolicy.DuplicateHash(new SubmitOrderRequest {
    Backfill = backfill, Meals = [new() { ReferenceId = "different-meal", Quantity = 2 }] }), "different products are not duplicates");
TipOrders.PolicyChecks();
if (args.Contains("--integration")) await Integration.Run();
Console.WriteLine("Order backfill checks passed.");

static void Throws(Action action) { try { action(); } catch (BusinessException) { return; } throw new Exception("Expected business rejection"); }
static void Equal<T>(T actual, T expected, string label) { if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception(label); }
static void Check(bool value, string label) { if (!value) throw new Exception(label); }
