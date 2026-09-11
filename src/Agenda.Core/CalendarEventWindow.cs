using System.Globalization;

namespace Agenda.Core;

public sealed record CalendarEventWindow(DateOnly Start,DateOnly EndExclusive)
{
    public bool Contains(CalendarEventWindow other) => Start<=other.Start && EndExclusive>=other.EndExclusive;

    public static CalendarEventWindow Around(DateOnly month,CultureInfo culture,bool isoWeeks,int adjacentMonths)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(adjacentMonths);
        // Include all six displayed weeks, not just dates inside each calendar month.
        var anchor=CalendarMath.MonthStart(month,culture);
        var first=CalendarMath.BuildMonth(CalendarMath.AddMonth(anchor,-adjacentMonths,culture),culture,isoWeeks);
        var last=CalendarMath.BuildMonth(CalendarMath.AddMonth(anchor,adjacentMonths,culture),culture,isoWeeks);
        return new(first[0].Days[0],last[^1].Days[^1].AddDays(1));
    }
}
