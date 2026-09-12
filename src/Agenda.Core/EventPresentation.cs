namespace Agenda.Core;

public static class EventPresentation
{
    public static bool HasEnded(CalendarEvent item,DateTimeOffset now,TimeZoneInfo zone) =>
        item.IsAllDay && item.AllDayEnd is { } end
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,zone).DateTime)>=end
            : item.End<=now;
}
