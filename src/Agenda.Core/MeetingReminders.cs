using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Agenda.Core;

public sealed record EventReminderOptions(List<int> MinutesBefore, DateTimeOffset ExpiresAt, string? Sound = null);
public sealed record MeetingReminder(CalendarEvent Event, int MinutesBefore, string EventKey, string DeliveryKey);

public static class MeetingReminders
{
    public static readonly IReadOnlyList<int> LeadTimes = Array.AsReadOnly(new[] { 0, 5, 10, 15, 30, 60 });
    // Store a digest, never event titles, subscription URLs, or raw calendar IDs.
    public static string Key(string source, CalendarEvent item) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] {
            source, item.CalendarId, item.Id, item.Start.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }))));

    public static IReadOnlyList<MeetingReminder> Due(IEnumerable<CalendarEvent> events,
        ISet<string> visibleCalendars, IReadOnlyDictionary<string,DateTimeOffset> sent,
        string source, DateTimeOffset now, IEnumerable<int>? defaults = null,
        IReadOnlyDictionary<string,EventReminderOptions>? overrides = null) => events
        .Where(item => !item.IsAllDay && visibleCalendars.Contains(item.CalendarId)
            && item.End > now)
        .SelectMany(item => {
            var key=Key(source,item);
            var times=overrides is not null && overrides.TryGetValue(key,out var custom)
                ? custom.MinutesBefore : defaults ?? new[] {5};
            return times.Where(LeadTimes.Contains).Distinct().Select(minutes =>
                new MeetingReminder(item,minutes,key,$"{key}:{minutes}"));
        })
        // A short grace period tolerates timer jitter without a burst of missed
        // reminders after startup, a long sleep, or a change of lead times.
        .Where(reminder => reminder.Event.Start.AddMinutes(-reminder.MinutesBefore) <= now
            && reminder.Event.Start.AddMinutes(-reminder.MinutesBefore) > now.AddMinutes(-1)
            && !sent.ContainsKey(reminder.DeliveryKey))
        .DistinctBy(reminder => reminder.DeliveryKey).OrderBy(reminder => reminder.Event.Start).ToArray();
}
