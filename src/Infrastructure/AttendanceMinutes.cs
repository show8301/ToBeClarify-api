namespace ToBeClarify.Api.Infrastructure;

public sealed class AttendanceMinuteEvent
{
    public string Id { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime EventAt { get; set; }
    public DateTime? EventEndAt { get; set; }
    public string? BaseEventId { get; set; }
    public int MinutesDelta { get; set; }
    public string Source { get; set; } = string.Empty;
}

public static class AttendanceMinutes
{
    public static int Effective(IEnumerable<AttendanceMinuteEvent> source)
    {
        var rows = source.ToArray();
        return Math.Max(0, Worked(rows) + rows.Where(x => x.EventType == "adjustment").Sum(x => x.MinutesDelta));
    }

    public static int Worked(IEnumerable<AttendanceMinuteEvent> source)
    {
        var rows = source.ToArray();
        var intervals = new List<(DateTime Start, DateTime End)>();
        var manual = rows.Where(x => x.EventType == "manual_interval" && x.EventEndAt is not null)
            .Select(x => (x.EventAt, x.EventEndAt!.Value)).ToArray();
        if (manual.Length > 0) intervals.AddRange(manual);
        else
        {
            var starts = rows.Where(x => x.EventType == "clock_in")
                .ToDictionary(x => x.Id, x => x.EventAt, StringComparer.Ordinal);
            intervals.AddRange(rows.Where(x => x.EventType == "clock_out" && x.BaseEventId is not null && starts.ContainsKey(x.BaseEventId))
                .Select(x => (starts[x.BaseEventId!], x.EventAt)));
            if (intervals.Count == 0)
                intervals.AddRange(rows.Where(x => x.EventType == "scheduled_end" && x.MinutesDelta > 0)
                    .Select(x => (x.EventAt.AddMinutes(-x.MinutesDelta), x.EventAt)));
        }
        var total = 0d;
        DateTime? start = null;
        DateTime? end = null;
        foreach (var interval in intervals.Where(x => x.End > x.Start).OrderBy(x => x.Start).ThenBy(x => x.End))
        {
            if (start is null) { start = interval.Start; end = interval.End; }
            else if (interval.Start <= end) end = interval.End > end ? interval.End : end;
            else { total += (end!.Value - start.Value).TotalMinutes; start = interval.Start; end = interval.End; }
        }
        if (start is not null) total += (end!.Value - start.Value).TotalMinutes;
        return Math.Max(0, (int)Math.Floor(total));
    }
}
