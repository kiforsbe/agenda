namespace Agenda.Core;

// Bounded to two snapshots: the browsing buffer and a buffer pinned around today.
public sealed class CalendarEventMemory
{
    private sealed record Snapshot(CalendarEventWindow Window,IReadOnlyList<CalendarEvent> Events,DateTimeOffset RefreshedAt)
    {
        public bool CanReuse(CalendarEventWindow required,DateTimeOffset now) =>
            Window.Contains(required) && now-RefreshedAt<TimeSpan.FromMinutes(5);
    }

    private Snapshot? _browsing;
    private Snapshot? _current;

    public void StoreBrowsing(CalendarEventWindow window,IReadOnlyList<CalendarEvent> events,DateTimeOffset now) =>
        _browsing=new(window,events,now);

    public void PinCurrent(CalendarEventWindow window,IReadOnlyList<CalendarEvent> events,DateTimeOffset now) =>
        _current=new(window,events,now);

    public bool CurrentCanReuse(CalendarEventWindow required,DateTimeOffset now) => _current?.CanReuse(required,now)==true;

    public bool CanReuse(CalendarEventWindow required,DateTimeOffset now) =>
        _browsing?.CanReuse(required,now)==true || CurrentCanReuse(required,now);

    public IReadOnlyList<CalendarEvent> Events(TimeZoneInfo zone)
    {
        if(_browsing is null)return _current?.Events??[];
        if(_current is null)return _browsing.Events;
        if(ReferenceEquals(_browsing.Events,_current.Events))return _browsing.Events;
        var newer=_browsing.RefreshedAt>=_current.RefreshedAt?_browsing:_current;
        var older=ReferenceEquals(newer,_browsing)?_current:_browsing;
        // An authoritative refresh may contain no events. Do not resurrect removed
        // appointments from an older snapshot merely because their IDs are absent.
        var start=newer.Window.Start.ToDateTime(TimeOnly.MinValue);
        var end=newer.Window.EndExclusive.ToDateTime(TimeOnly.MinValue);
        var startOffset=new DateTimeOffset(start,zone.GetUtcOffset(start));
        var endOffset=new DateTimeOffset(end,zone.GetUtcOffset(end));
        bool Overlaps(CalendarEvent item) => item.IsAllDay && item.AllDayStart is { } first && item.AllDayEnd is { } last
            ? first<newer.Window.EndExclusive && last>newer.Window.Start
            : item.Start<endOffset && item.End>startOffset;
        return newer.Events.Concat(older.Events.Where(item=>!Overlaps(item)))
            .DistinctBy(item=>(item.CalendarId,item.Id)).OrderBy(item=>item.Start).ToArray();
    }

    public void Clear() { _browsing=null;_current=null; }
}
