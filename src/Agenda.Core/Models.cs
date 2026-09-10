namespace Agenda.Core;

public sealed record CalendarSource(string Id, string Name, string Color, bool CanEdit, string AccountName);
public sealed record CalendarEvent(string Id, string CalendarId, string Subject,
    DateTimeOffset Start, DateTimeOffset End, bool IsAllDay, string Location,
    string? WebLink = null, string? JoinUrl = null, DateOnly? AllDayStart = null, DateOnly? AllDayEnd = null);
public sealed record EventDraft(string CalendarId, string Subject, DateTimeOffset Start,
    DateTimeOffset End, bool IsAllDay, string Location, string? Notes = null);
public interface ICalendarProvider
{
    string? AccountName { get; }
    Task SignInAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CalendarSource>> GetCalendarsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(IReadOnlyList<CalendarSource> calendars,
        DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);
    Task CreateEventAsync(EventDraft draft, CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
