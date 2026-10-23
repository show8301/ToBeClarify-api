using ToBeClarify.Api.Exceptions;
using ToBeClarify.Api.Repositories.Shared;

// Pure policy regression cases. No database, credentials, HTTP or real orders.
var start = new DateTime(2026, 10, 23, 20, 0, 0);
var end = start.AddHours(6);
var schedule = new NominationSchedule
{
    StaffId = "sample", IsAccepting = true, ShiftStartsAt = start, ShiftEndsAt = end, BufferMinutes = 10
};
Check(schedule.Status(start.AddMinutes(-30), end, 20) == "before_shift", "Clock-in must not move the approved shift start.");
Conflict(() => schedule.Validate(start.AddMinutes(-10), start.AddMinutes(20)), "NOMINATION_OUTSIDE_SHIFT");
schedule.Validate(start, start.AddMinutes(30));
Check(schedule.Status(end, end, 20) == "ended", "Cross-midnight shift end is exclusive.");
Conflict(() => schedule.Validate(end.AddMinutes(-20), end.AddMinutes(10)), "NOMINATION_OUTSIDE_SHIFT");

schedule.Occupied = [new() { StartsAt = start.AddHours(1), EndsAt = end }];
Check(schedule.Status(start.AddHours(1), end, 20) == "fully_booked", "Reservation through close has no normal availability.");
Check(schedule.Status(start, end, 20) == "partially_reserved", "A future reservation does not hide the current free gap.");
Conflict(() => schedule.Validate(start.AddHours(2), start.AddHours(3)), "STAFF_TIME_CONFLICT");
schedule.Validate(start.AddMinutes(30), start.AddHours(1));
Check(schedule.FreeWindows(start, end).Single().EndsAt == start.AddHours(1), "Rest-inclusive occupied ranges leave exact adjacent boundaries valid.");

schedule.Occupied = [
    new() { StartsAt = start.AddHours(1), EndsAt = start.AddHours(2) },
    new() { StartsAt = start.AddMinutes(90), EndsAt = start.AddHours(3) },
    new() { StartsAt = start.AddHours(3), EndsAt = start.AddHours(4) }
];
var windows = schedule.FreeWindows(start, end);
Check(windows.Count == 2 && windows[0].EndsAt == start.AddHours(1) && windows[1].StartsAt == start.AddHours(4),
    "Overlapping and adjacent reservations cannot generate phantom gaps.");
var close = start.AddMinutes(20);
Check(schedule.Status(start, close, 20) == "fully_booked", "A gap too short for service plus buffer is full.");
Check(schedule.FreeWindows(start, end).Count > 0, "A normal closing cutoff does not destroy potential coordination windows within the shift.");
schedule.IsAccepting = false;
Check(schedule.FreeWindows(start, end).Count == 0 && schedule.Status(start, end, 20) == "paused", "Stopped intake prevents new reservations.");
Conflict(() => schedule.Validate(start, start.AddMinutes(30)), "NOMINATION_UNAVAILABLE");
schedule.Validate(start, start.AddMinutes(30), requireAccepting: false);
schedule.IsAccepting = true;
schedule.IsOverdue = true;
Check(schedule.Status(start, end, 20) == "overdue", "An unfinished overdue service must not be advertised as available.");
Check(schedule.FreeWindows(start, end).Count == 0, "Unknown release time fails closed even without a normal busy interval.");
Console.WriteLine("Nomination availability policy checks passed.");

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
}

static void Conflict(Action action, string code)
{
    try { action(); }
    catch (ConflictException error) when (error.ErrorCode == code) { return; }
    throw new InvalidOperationException($"Expected {code}.");
}
