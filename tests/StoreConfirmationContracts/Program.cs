using System.Reflection;
using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Models.Dtos;
using ToBeClarify.Api.Repositories.Ordering;

// In-memory regression checks only. No host, database, credentials or cash writes.
var summary = typeof(OrderingRepository).GetMethod("SummarizeFulfillmentStatus", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("Fulfillment status calculation is missing.");
var rejection = typeof(OrderingRepository).GetMethod("ValidateFulfillmentStoreRejection", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("Store rejection validation is missing.");
var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    passed++;
    Console.WriteLine($"PASS {label}");
}
string Status(params FulfillmentUnitDto[] units) => (string)summary.Invoke(null, [units])!;
int Credit(int remaining, params FulfillmentUnitDto[] units) => (int)rejection.Invoke(null, [units, remaining])!;
void Reject(Action action, string code, string label)
{
    try { action(); throw new Exception($"Expected {code}: {label}"); }
    catch (TargetInvocationException error) when
        ((error.InnerException as BusinessException)?.ErrorCode == code ||
         (error.InnerException as ConflictException)?.ErrorCode == code)
    { Check(true, label); }
}
FulfillmentUnitDto Unit(string kind = "nominee", int quantity = 1, int credit = 0) => new(
    Id: Guid.NewGuid().ToString("D"), OrderItemId: null, NomineeId: null, Kind: kind, Name: "test",
    StaffId: null, Quantity: quantity, AcceptedQuantity: 0, StartedQuantity: 0, CompletedQuantity: 0,
    CancelledQuantity: 0, Status: "waiting", Version: 1, OriginalAmount: 100_000, OriginalCredit: credit,
    CancelledAmount: 0, ReturnedCredit: 0, PurchasedMinutes: 20, ScheduledStartsAt: null, ScheduledEndsAt: null,
    ActualStartsAt: null, ActualEndsAt: null, OriginalScheduledStartsAt: null, OriginalScheduledEndsAt: null,
    RestMinutesReserved: 0, FulfillmentPeriodId: null, AllowedActions: []);

var nominee = Unit();
var meal = Unit("meal", 3, 10_000);
var addon = Unit("addon");
var tip = Unit("tip") with { Status = "completed", AcceptedQuantity = 1, StartedQuantity = 1, CompletedQuantity = 1 };
Check(Status(nominee) == "submitted", "store approval still waits for the nominated staff member");
Check(Status(addon) == "submitted", "store approval does not accept an add-on service");
Check(Status(meal) == "confirmed", "meal-only order can clear the store gate without recording delivery");
Check(Status(meal, nominee, tip) == "submitted", "completed tip does not imply acceptance of another service");
Check(Status(meal with { AcceptedQuantity = 1 }, nominee) == "partially_confirmed", "mixed order retains partial acceptance");
Check(Status(nominee with { AcceptedQuantity = 1, StartedQuantity = 1 }) == "in_service", "existing service progress is retained");
Check(Status(tip) == "completed", "tip-only order can be approved despite automatic allocation completion");
Check(Status(nominee with { CancelledQuantity = 1, Status = "cancelled" }) == "cancelled", "cancelled units do not reopen the order");
Check(Status(tip, nominee with { CancelledQuantity = 1, Status = "cancelled" }) == "completed", "finished and cancelled units retain completion");
Check(Status(nominee with { Status = "needs_coordination" }) == "needs_reschedule", "staff coordination remains a separate step");
Check(Credit(10_000, meal, nominee, addon, tip) == 10_000, "pending order rejection returns only the reserved meal credit");
Check(Credit(0, tip) == 0, "automatic tip completion does not prevent rejecting an unaccepted order");
var partial = Unit("meal", 3, 300) with { CancelledQuantity = 1, CancelledAmount = 33_333, ReturnedCredit = 100 };
Check(Credit(200, partial) == 200, "previously returned credit is not returned twice");
Check(Credit(1, Unit("meal", 3, 1) with { CancelledQuantity = 1, CancelledAmount = 33_333 }) == 1,
    "integer allocation remainder is returned when the remaining order is rejected");
Reject(() => Credit(300, partial), "FULFILLMENT_STORE_STATE_CONFLICT", "mismatched remaining credit is rejected");
Reject(() => Credit(300, partial with { ReturnedCredit = 0 }), "FULFILLMENT_STORE_STATE_CONFLICT", "cancelled quantities and returned credit must agree");
Reject(() => Credit(0, nominee with { AcceptedQuantity = 1, StartedQuantity = 1 }), "FULFILLMENT_STORE_REJECTION_STARTED", "started services cannot be erased by store rejection");
Reject(() => Credit(0, nominee with { AcceptedQuantity = 1, StartedQuantity = 1, CompletedQuantity = 1, Status = "completed" }), "FULFILLMENT_STORE_REJECTION_STARTED", "completed services cannot be erased by store rejection");
Reject(() => Credit(0, Unit("room") with { AcceptedQuantity = 1, StartedQuantity = 1 }), "FULFILLMENT_STORE_REJECTION_STARTED", "room delivery receives the same protection");
Reject(() => Credit(0, nominee with { Quantity = 0 }), "FULFILLMENT_STORE_STATE_CONFLICT", "invalid quantities are not silently repaired");
Reject(() => Credit(0, nominee with { AcceptedQuantity = 1, CancelledQuantity = 1 }), "FULFILLMENT_STORE_STATE_CONFLICT", "overlapping accepted and cancelled counters are rejected");
Reject(() => Credit(0, meal with { ReturnedCredit = 10_001 }), "FULFILLMENT_STORE_STATE_CONFLICT", "over-returned credit is rejected");
Reject(() => Credit(0), "FULFILLMENT_STORE_STATE_CONFLICT", "missing fulfillment cannot be rejected as an empty sale");
Reject(() => Credit(int.MaxValue, Unit("meal", credit: int.MaxValue), Unit("meal", credit: int.MaxValue)), "FULFILLMENT_STORE_STATE_CONFLICT", "credit totals do not overflow to a smaller refund");
var snapshot = new[] { meal, nominee, tip };
Status(snapshot);
Credit(10_000, snapshot);
Check(snapshot[0] == meal && snapshot[1] == nominee && snapshot[2] == tip, "validation and status aggregation never mutate service counters");
Console.WriteLine($"{passed} checks passed; no database or HTTP mutations.");
