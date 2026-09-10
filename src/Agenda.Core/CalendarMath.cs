using System.Globalization;
namespace Agenda.Core;

public sealed record WeekLabel(int Year, int Number);
public sealed record MonthWeek(WeekLabel Week, IReadOnlyList<DateOnly> Days);
public static class CalendarMath
{
    public static WeekLabel GetWeek(DateOnly day, DayOfWeek firstDay, CalendarWeekRule rule)
    {
        var year = day.Year;
        var start = FirstWeekStart(year, firstDay, rule);
        if (day.DayNumber < start && year > 1) start = FirstWeekStart(--year, firstDay, rule);
        else if (year < 9999 && day.DayNumber >= FirstWeekStart(year + 1, firstDay, rule)) start = FirstWeekStart(++year, firstDay, rule);
        return new(year, (day.DayNumber - start) / 7 + 1);
    }

    private static int FirstWeekStart(int year, DayOfWeek firstDay, CalendarWeekRule rule)
    {
        var jan1 = new DateOnly(year, 1, 1);
        var offset = ((int)jan1.DayOfWeek - (int)firstDay + 7) % 7;
        var minimum = rule switch { CalendarWeekRule.FirstDay => 1, CalendarWeekRule.FirstFullWeek => 7, _ => 4 };
        return jan1.DayNumber - offset + (7 - offset < minimum ? 7 : 0);
    }

    public static IReadOnlyList<MonthWeek> BuildMonth(DateOnly anchor, CultureInfo culture, bool isoWeeks)
    {
        var firstDay = isoWeeks ? DayOfWeek.Monday : culture.DateTimeFormat.FirstDayOfWeek;
        var rule = isoWeeks ? CalendarWeekRule.FirstFourDayWeek : culture.DateTimeFormat.CalendarWeekRule;
        var first = MonthStart(anchor, culture);
        var offset = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var start = first.AddDays(-offset);
        return Enumerable.Range(0, 6).Select(row => {
            var days = Enumerable.Range(0, 7).Select(column => start.AddDays(row * 7 + column)).ToArray();
            return new MonthWeek(isoWeeks ? GetWeek(days[0], firstDay, rule) : GetRegionalWeek(days[0], culture), days);
        }).ToArray();
    }

    public static DateOnly MonthStart(DateOnly day, CultureInfo culture) =>
        day.AddDays(1 - culture.DateTimeFormat.Calendar.GetDayOfMonth(day.ToDateTime(TimeOnly.MinValue)));

    public static DateOnly AddMonth(DateOnly day, int count, CultureInfo culture) =>
        DateOnly.FromDateTime(culture.DateTimeFormat.Calendar.AddMonths(day.ToDateTime(TimeOnly.MinValue), count));

    public static WeekLabel GetRegionalWeek(DateOnly day, CultureInfo culture)
    {
        var calendar=culture.DateTimeFormat.Calendar;
        var firstDay=culture.DateTimeFormat.FirstDayOfWeek;
        var minimum=culture.DateTimeFormat.CalendarWeekRule switch { CalendarWeekRule.FirstDay=>1, CalendarWeekRule.FirstFullWeek=>7,_=>4 };
        int WeekStart(DateOnly first) {
            var offset=((int)first.DayOfWeek-(int)firstDay+7)%7;
            return first.DayNumber-offset+(7-offset<minimum?7:0);
        }
        var yearStart=day.AddDays(1-calendar.GetDayOfYear(day.ToDateTime(TimeOnly.MinValue)));
        var nextYear=DateOnly.FromDateTime(calendar.AddYears(yearStart.ToDateTime(TimeOnly.MinValue),1));
        if (day.DayNumber<WeekStart(yearStart)) yearStart=DateOnly.FromDateTime(calendar.AddYears(yearStart.ToDateTime(TimeOnly.MinValue),-1));
        else if (day.DayNumber>=WeekStart(nextYear)) yearStart=nextYear;
        return new(calendar.GetYear(yearStart.ToDateTime(TimeOnly.MinValue)),(day.DayNumber-WeekStart(yearStart))/7+1);
    }

    public static IReadOnlyList<CalendarSource> SourcesOnDay(IEnumerable<CalendarEvent> events,
        IEnumerable<CalendarSource> visibleCalendars, DateOnly day, TimeZoneInfo zone)
    {
        var active=events.Where(item=>OccursOn(item,day,zone)).Select(item=>item.CalendarId).ToHashSet(StringComparer.Ordinal);
        // One segment per source, in calendar order, regardless of event count or duplicate colors.
        return visibleCalendars.Where(calendar=>active.Contains(calendar.Id)).DistinctBy(calendar=>calendar.Id).ToArray();
    }

    public static bool OccursOn(CalendarEvent item, DateOnly day, TimeZoneInfo zone)
    {
        if (item.IsAllDay && item.AllDayStart is { } begin && item.AllDayEnd is { } end)
            return begin <= day && day < end;
        var localStart = TimeZoneInfo.ConvertTime(item.Start, zone).DateTime;
        var localEnd = TimeZoneInfo.ConvertTime(item.End, zone).DateTime;
        var startOfDay = day.ToDateTime(TimeOnly.MinValue);
        return localStart < startOfDay.AddDays(1) && localEnd > startOfDay;
    }
}
