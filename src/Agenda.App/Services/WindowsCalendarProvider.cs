using Agenda.Core;
using Windows.ApplicationModel.Appointments;

namespace Agenda.App.Services;

/// <summary>Reads only calendars actually exposed by the Windows appointment store.</summary>
public sealed class WindowsCalendarProvider : ICalendarProvider
{
    private AppointmentStore? _store;
    private IReadOnlyList<AppointmentCalendar> _calendars = [];
    public string? AccountName => _store is null ? null : "Windows calendars";
    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        _store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly).AsTask(cancellationToken);
        if (_store is null) throw new InvalidOperationException("Windows did not grant access to its calendar store. Use a calendar feed instead.");
        _calendars = await _store.FindAppointmentCalendarsAsync().AsTask(cancellationToken);
        if (_calendars.Count == 0) {
            _store=null;
            throw new InvalidOperationException("Windows exposes no calendars to this app. New Outlook does not necessarily populate this store. Connect a Google or Outlook calendar feed instead.");
        }
    }
    public async Task<IReadOnlyList<CalendarSource>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_store is null) throw new InvalidOperationException("Choose Use Windows calendars first.");
        _calendars=await _store.FindAppointmentCalendarsAsync().AsTask(cancellationToken);
        return _calendars.Select(c=>new CalendarSource(c.LocalId,c.DisplayName,"#0078D4",false,c.SourceDisplayName)).ToArray();
    }
    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(IReadOnlyList<CalendarSource> calendars,DateTimeOffset start,DateTimeOffset end,CancellationToken cancellationToken=default)
    {
        if (_store is null) throw new InvalidOperationException("Windows calendar access is not connected.");
        var result=new List<CalendarEvent>();
        foreach(var source in calendars) {
            var calendar=_calendars.First(c=>c.LocalId==source.Id);
            var options=new FindAppointmentsOptions { IncludeHidden=true };
            options.FetchProperties.Add(AppointmentProperties.Subject);
            options.FetchProperties.Add(AppointmentProperties.StartTime);
            options.FetchProperties.Add(AppointmentProperties.Duration);
            options.FetchProperties.Add(AppointmentProperties.Location);
            options.FetchProperties.Add(AppointmentProperties.AllDay);
            var appointments=await calendar.FindAppointmentsAsync(start,end-start,options).AsTask(cancellationToken);
            foreach(var appointment in appointments) {
                var allDayStart=DateOnly.FromDateTime(appointment.StartTime.DateTime);
                result.Add(new CalendarEvent(appointment.LocalId,source.Id,appointment.Subject,appointment.StartTime,appointment.StartTime+appointment.Duration,appointment.AllDay,appointment.Location,
                    AllDayStart:appointment.AllDay?allDayStart:null,
                    AllDayEnd:appointment.AllDay?allDayStart.AddDays(Math.Max(1,(int)Math.Ceiling(appointment.Duration.TotalDays))):null));
            }
        }
        return result;
    }
    public Task CreateEventAsync(EventDraft draft,CancellationToken cancellationToken=default) => throw new InvalidOperationException("Windows calendar access is read-only. Create events in your calendar application.");
    public Task SignOutAsync(CancellationToken cancellationToken=default) { _store=null;_calendars=[];return Task.CompletedTask; }
}
