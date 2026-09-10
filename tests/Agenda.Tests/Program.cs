using System.Globalization;
using Agenda.Core;

var failures = 0;
var count=0;
void Check(string name, Action test) { count++; try { test(); Console.WriteLine($"PASS {name}"); } catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e.Message}"); } }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
Check("ISO early January belongs to preceding week year", () => Equal(new WeekLabel(2020,53), CalendarMath.GetWeek(new(2021,1,1), DayOfWeek.Monday, CalendarWeekRule.FirstFourDayWeek)));
Check("ISO first Monday starts week one", () => Equal(new WeekLabel(2021,1), CalendarMath.GetWeek(new(2021,1,4), DayOfWeek.Monday, CalendarWeekRule.FirstFourDayWeek)));
Check("ISO late December belongs to following week year", () => Equal(new WeekLabel(2019,1), CalendarMath.GetWeek(new(2018,12,31), DayOfWeek.Monday, CalendarWeekRule.FirstFourDayWeek)));
Check("US week containing January 1 starts week one", () => Equal(new WeekLabel(2022,1), CalendarMath.GetWeek(new(2021,12,26), DayOfWeek.Sunday, CalendarWeekRule.FirstDay)));
Check("First full week rule keeps January 1 in preceding year", () => Equal(new WeekLabel(2020,52), CalendarMath.GetWeek(new(2021,1,1), DayOfWeek.Monday, CalendarWeekRule.FirstFullWeek)));
Check("Sunday first month includes preceding Sunday", () => { var grid = CalendarMath.BuildMonth(new(2026,9,9), new CultureInfo("en-US"), false); Equal(new DateOnly(2026,8,30),grid[0].Days[0]); Equal(6,grid.Count); Equal(new DateOnly(2026,10,10),grid[5].Days[6]); });
Check("User first weekday override wins over culture", () => { var c = new CultureInfo("en-US"); c.DateTimeFormat.FirstDayOfWeek=DayOfWeek.Saturday; Equal(new DateOnly(2026,8,29),CalendarMath.BuildMonth(new(2026,9,9),c,false)[0].Days[0]); });
Check("ISO override uses Monday even with US regional settings", () => Equal(new DateOnly(2026,8,31),CalendarMath.BuildMonth(new(2026,9,9),new CultureInfo("en-US"),true)[0].Days[0]));
Check("Swedish regional week agrees with ISO at year rollover", () => Equal(1,CalendarMath.BuildMonth(new(2019,1,1),new CultureInfo("sv-SE"),false)[0].Week.Number));
Check("Multi-day event appears on each overlapping local day", () => { var e = new CalendarEvent("1","c","Trip",new(2026,9,9,23,0,0,TimeSpan.Zero),new(2026,9,11,0,0,0,TimeSpan.Zero),false,""); Equal(true,CalendarMath.OccursOn(e,new(2026,9,10),TimeZoneInfo.Utc)); Equal(false,CalendarMath.OccursOn(e,new(2026,9,11),TimeZoneInfo.Utc)); });
Check("All-day event end date is exclusive regardless of timezone", () => { var e=new CalendarEvent("1","c","Day",DateTimeOffset.MinValue,DateTimeOffset.MinValue,true,"",AllDayStart:new(2026,9,9),AllDayEnd:new(2026,9,10)); Equal(true,CalendarMath.OccursOn(e,new(2026,9,9),TimeZoneInfo.Utc)); Equal(false,CalendarMath.OccursOn(e,new(2026,9,10),TimeZoneInfo.Utc)); });
Check("Local event date uses selected timezone", () => { var e=new CalendarEvent("1","c","Call",new(2026,9,9,23,30,0,TimeSpan.Zero),new(2026,9,10,0,0,0,TimeSpan.Zero),false,""); Equal(true,CalendarMath.OccursOn(e,new(2026,9,10),TimeZoneInfo.CreateCustomTimeZone("test",TimeSpan.FromHours(2),"test","test"))); });
Check("Persian regional New Year uses Persian week one", () => Equal(new WeekLabel(1405,1),CalendarMath.BuildMonth(new(2026,3,21),new CultureInfo("fa-IR"),false)[0].Week));
Check("ISO override stays Gregorian in a Persian month", () => Equal(new WeekLabel(2026,12),CalendarMath.BuildMonth(new(2026,3,21),new CultureInfo("fa-IR"),true)[0].Week));
Check("Feed colors avoid repeats until the palette is exhausted", () => {
    var used=new List<string>();
    foreach(var color in FeedColorPalette.Colors) { var next=FeedColorPalette.PickNext(used);Equal(color.Hex,next);used.Add(next); }
});
Check("Feed colors balance reuse and ignore hex letter casing", () => {
    var used=FeedColorPalette.Colors.Select(color=>color.Hex.ToLowerInvariant()).ToList();
    used.Add(FeedColorPalette.Colors[0].Hex);
    Equal(FeedColorPalette.Colors[1].Hex,FeedColorPalette.PickNext(used));
});
Check("Custom feed colors do not consume palette slots", () => Equal(FeedColorPalette.Colors[0].Hex,FeedColorPalette.PickNext(["#123456"])));
Check("Day markers group events once per source in calendar order", () => {
    var calendars=new[] { new CalendarSource("blue","Blue","#0078D4",false,"Feeds"),new CalendarSource("gold","Gold","#E9B44C",false,"Feeds"),new CalendarSource("green","Green","#82C986",false,"Feeds") };
    var day=new DateOnly(2026,9,10);
    for(var count=0;count<=3;count++) {
        var events=calendars.Take(count).Reverse().SelectMany(source=>Enumerable.Range(0,2).Select(i=>new CalendarEvent($"{source.Id}-{i}",source.Id,"Event",new(2026,9,10,8,0,0,TimeSpan.Zero),new(2026,9,10,9,0,0,TimeSpan.Zero),false,""))).ToArray();
        var result=CalendarMath.SourcesOnDay(events,calendars,day,TimeZoneInfo.Utc);
        Equal(count,result.Count);
        Equal(string.Join(",",calendars.Take(count).Select(c=>c.Id)),string.Join(",",result.Select(c=>c.Id)));
    }
});
Check("Day markers respect visibility and retain separate sources with matching colors", () => {
    var sources=new[] { new CalendarSource("a","A","#0078D4",false,"Feeds"),new CalendarSource("b","B","#0078D4",false,"Feeds") };
    var events=sources.Select(source=>new CalendarEvent(source.Id,source.Id,"Event",new(2026,9,10,8,0,0,TimeSpan.Zero),new(2026,9,10,9,0,0,TimeSpan.Zero),false,"")).ToArray();
    Equal(2,CalendarMath.SourcesOnDay(events,sources,new(2026,9,10),TimeZoneInfo.Utc).Count);
    Equal("b",CalendarMath.SourcesOnDay(events,sources.Skip(1),new(2026,9,10),TimeZoneInfo.Utc).Single().Id);
    Equal(0,CalendarMath.SourcesOnDay(events,sources,new(2026,9,11),TimeZoneInfo.Utc).Count);
});
Console.WriteLine($"{count-failures}/{count} checks passed");
return failures==0?0:1;
