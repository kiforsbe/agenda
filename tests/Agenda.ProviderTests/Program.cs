using System.Net;
using System.Text;
using System.Text.Json;
using Agenda.Core;
using Agenda.Providers;

var tests = new (string Name, Func<Task> Run)[]
{
    ("calendar paging follows Graph nextLink", CalendarPagingAsync),
    ("paging rejects a nextLink on another host", PagingRejectsOtherHostAsync),
    ("paging stops after the configured page bound", PagingBoundAsync),
    ("calendarView preserves timed offsets and all-day dates", EventTimesAndAllDayDatesAsync),
    ("Graph errors retain status, code, and message", GraphErrorAsync),
    ("read-only calendars reject writes before transport", ReadOnlyWriteAsync),
    ("event creation sends a Graph calendar payload", CreatePayloadAsync),
    ("protected cache round-trips without plaintext", ProtectedCacheAsync),
    ("calendar reads never trigger interactive sign-in", ReadsNeverPromptAsync),
    ("event save requests write consent incrementally", IncrementalWriteConsentAsync),
    ("offline event reads return protected cache marked stale", OfflineCacheAsync),
    ("offline calendar enumeration returns protected cache marked stale", OfflineCalendarCacheAsync),
    ("sign-out removes the protected event cache", SignOutClearsCacheAsync),
    ("sign-out removes protected cache even when account removal fails", SignOutFailureStillClearsCacheAsync),
    ("public provider validates its client ID", ClientIdValidation),
    ("ICS recurrence preserves timezone offsets and EXDATE", IcsRecurrenceAsync),
    ("ICS multi-day all-day events preserve exclusive dates", IcsMultiDayAllDayAsync),
    ("invalid ICS content raises a feed error", InvalidIcsRaisesErrorAsync),
    ("ICS feeds are read-only", IcsReadOnlyAsync),
    ("feed configuration storage encrypts secret URLs", FeedConfigurationEncryptionAsync),
    ("feed locations reject insecure network schemes", FeedLocationValidation),
    ("offline ICS downloads return encrypted cache marked stale", IcsOfflineCacheAsync),
    ("ICS refresh rejects HTTPS redirects to plaintext", IcsRejectsDowngradeRedirectAsync),
    ("ICS failed feeds retain successful feeds and redact private URLs", IcsPartialFailureAsync),
    ("ICS recurrence replacements and overlapping all-day events survive range filtering", IcsOverridesAndOverlapAsync),
    ("ICS dense recurrence expansion has a finite occurrence bound", IcsOccurrenceLimitAsync),
    ("ICS cancelled refresh propagates cancellation", IcsCancellationAsync),
    ("ICS malformed refresh falls back to previous encrypted feed", IcsMalformedCacheAsync),
    ("ICS input byte limit rejects oversized files", IcsByteLimitAsync),
    ("ICS cancellation after download is preserved", IcsCancellationAfterDownloadAsync),
    ("Microsoft request timeouts use stale calendar and event caches", MicrosoftTimeoutCacheAsync),
    ("ICS rejects obsolete exclusion rules before recurrence expansion", IcsRejectsExruleAsync),
    ("ICS stalled response bodies respect the whole-download deadline", IcsBodyDeadlineAsync),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
    }
}

return failures == 0 ? 0 : 1;

static async Task CalendarPagingAsync()
{
    var handler = new ScriptedHandler(
        Json(HttpStatusCode.OK, """{"value":[{"id":"one","name":"Home","hexColor":"#123456","canEdit":true,"owner":{"address":"me@example.com"}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/calendars?$skiptoken=next"}"""),
        Json(HttpStatusCode.OK, """{"value":[{"id":"two","name":"Birthdays","color":"lightBlue","canEdit":false,"owner":{"address":"me@example.com"}}]}"""));
    var client = new GraphCalendarClient(new HttpClient(handler));

    var result = await client.GetCalendarsAsync("access-token", "me@example.com", default);

    Equal(2, result.Count);
    Equal("#123456", result[0].Color);
    Equal(false, result[1].CanEdit);
    Equal(2, handler.Requests.Count);
    Equal("Bearer access-token", handler.Requests[0].Authorization);
    Contains("$skiptoken=next", handler.Requests[1].Uri.Query);
}

static async Task PagingRejectsOtherHostAsync()
{
    var handler = new ScriptedHandler(Json(HttpStatusCode.OK,
        """{"value":[],"@odata.nextLink":"https://example.invalid/steal"}"""));
    var client = new GraphCalendarClient(new HttpClient(handler));

    var error = await ThrowsAsync<GraphProtocolException>(() =>
        client.GetCalendarsAsync("secret-token", "me@example.com", default));

    Contains("host", error.Message);
    Equal(1, handler.Requests.Count);
}

static async Task PagingBoundAsync()
{
    var responses = Enumerable.Range(0, 100)
        .Select(_ => Json(HttpStatusCode.OK,
            """{"value":[],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/calendars?$skiptoken=loop"}"""))
        .ToArray();
    var handler = new ScriptedHandler(responses);
    var client = new GraphCalendarClient(new HttpClient(handler));

    var error = await ThrowsAsync<GraphProtocolException>(() =>
        client.GetCalendarsAsync("token", "me@example.com", default));

    Contains("more than 100 pages", error.Message);
    Equal(100, handler.Requests.Count);
}

static async Task EventTimesAndAllDayDatesAsync()
{
    var handler = new ScriptedHandler(Json(HttpStatusCode.OK, """
        {"value":[
          {"id":"timed","subject":"Call","isAllDay":false,"start":{"dateTime":"2026-07-01T09:00:00","timeZone":"Pacific Standard Time"},"end":{"dateTime":"2026-07-01T10:30:00","timeZone":"Pacific Standard Time"},"location":{"displayName":"Desk"},"webLink":"https://outlook.office.com/event","onlineMeeting":{"joinUrl":"https://teams.microsoft.com/join"}},
          {"id":"day","subject":"Holiday","isAllDay":true,"start":{"dateTime":"2026-12-24T00:00:00.0000000","timeZone":"Europe/Stockholm"},"end":{"dateTime":"2026-12-26T00:00:00.0000000","timeZone":"Europe/Stockholm"},"location":{"displayName":""}}
        ]}
        """));
    var client = new GraphCalendarClient(new HttpClient(handler));
    var calendars = new[] { new CalendarSource("cal/a", "Calendar", "auto", true, "me@example.com") };

    var result = await client.GetEventsAsync("token", calendars,
        new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), default);

    Equal(2, result.Count);
    Equal(TimeSpan.FromHours(-7), result[0].Start.Offset);
    Equal(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(-7)), result[0].Start);
    Equal("cal/a", result[0].CalendarId);
    Equal(new DateOnly(2026, 12, 24), result[1].AllDayStart);
    Equal(new DateOnly(2026, 12, 26), result[1].AllDayEnd);
    Contains("calendars/cal%2Fa/calendarView", handler.Requests[0].Uri.AbsoluteUri);
    Contains("startDateTime=2026-07-01T00%3A00%3A00.0000000%2B00%3A00", handler.Requests[0].Uri.AbsoluteUri);
}

static async Task GraphErrorAsync()
{
    var handler = new ScriptedHandler(Json(HttpStatusCode.Forbidden,
        """{"error":{"code":"ErrorAccessDenied","message":"The user denied calendar access."}}"""));
    var client = new GraphCalendarClient(new HttpClient(handler));

    var error = await ThrowsAsync<GraphApiException>(() =>
        client.GetCalendarsAsync("token", "me@example.com", default));

    Equal(HttpStatusCode.Forbidden, error.StatusCode);
    Equal("ErrorAccessDenied", error.ErrorCode);
    Contains("denied calendar access", error.Message);
}

static async Task ReadOnlyWriteAsync()
{
    var handler = new ScriptedHandler(Json(HttpStatusCode.Created, "{}"));
    var client = new GraphCalendarClient(new HttpClient(handler));
    var readOnly = new CalendarSource("readonly", "Holidays", "auto", false, "me@example.com");
    var draft = new EventDraft("readonly", "Attempt", DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddHours(1), false, "");

    await ThrowsAsync<CalendarReadOnlyException>(() =>
        client.CreateEventAsync("write-token", readOnly, draft, default));

    Equal(0, handler.Requests.Count);
}

static async Task CreatePayloadAsync()
{
    var handler = new ScriptedHandler(Json(HttpStatusCode.Created, """{"id":"created"}"""));
    var client = new GraphCalendarClient(new HttpClient(handler));
    var calendar = new CalendarSource("cal/id", "Work", "auto", true, "me@example.com");
    var draft = new EventDraft("cal/id", "Review", new DateTimeOffset(2026, 9, 9, 14, 0, 0, TimeSpan.FromHours(2)),
        new DateTimeOffset(2026, 9, 9, 15, 0, 0, TimeSpan.FromHours(2)), false, "Room 3", "Bring notes");

    await client.CreateEventAsync("write-token", calendar, draft, default);

    Equal(HttpMethod.Post, handler.Requests[0].Method);
    Contains("calendars/cal%2Fid/events", handler.Requests[0].Uri.AbsoluteUri);
    using var json = JsonDocument.Parse(handler.Requests[0].Body!);
    Equal("Review", json.RootElement.GetProperty("subject").GetString());
    Equal("Bring notes", json.RootElement.GetProperty("body").GetProperty("content").GetString());
    Equal("UTC", json.RootElement.GetProperty("start").GetProperty("timeZone").GetString());
    Equal("2026-09-09T12:00:00.0000000", json.RootElement.GetProperty("start").GetProperty("dateTime").GetString());
}

static async Task ProtectedCacheAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), "agenda-provider-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var cache = new ProtectedCalendarCache(directory);
        var events = new[]
        {
            new CalendarEvent("event-secret-id", "calendar-secret-id", "Private appointment",
                new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero), false, "Private room")
        };
        var query = new EventCacheQuery("account-id", new[] { "calendar-secret-id" },
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        await cache.SaveAsync(query, events, new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero), default);
        var bytes = await File.ReadAllBytesAsync(cache.CachePath);
        var diskText = Encoding.UTF8.GetString(bytes);
        DoesNotContain("Private appointment", diskText);
        DoesNotContain("event-secret-id", diskText);

        var loaded = await cache.TryLoadAsync(query, default);
        Equal("Private appointment", loaded!.Events[0].Subject);
        Equal(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero), loaded.RefreshedAt);
        cache.Clear();
        Equal(false, File.Exists(cache.CachePath));
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static async Task ReadsNeverPromptAsync()
{
    var directory = TestDirectory();
    try
    {
        var auth = new FakeTokenProvider { SilentError = new MicrosoftCalendarAuthenticationRequiredException("Connect first.") };
        var provider = new MicrosoftCalendarProvider(auth,
            new GraphCalendarClient(new HttpClient(new ScriptedHandler())),
            new ProtectedCalendarCache(directory));

        await ThrowsAsync<MicrosoftCalendarAuthenticationRequiredException>(() => provider.GetCalendarsAsync());

        Equal(1, auth.SilentCalls);
        Equal(0, auth.InteractiveCalls);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IncrementalWriteConsentAsync()
{
    var directory = TestDirectory();
    try
    {
        var handler = new ScriptedHandler(
            Json(HttpStatusCode.OK, """{"value":[{"id":"editable","name":"Calendar","canEdit":true}]}"""),
            Json(HttpStatusCode.Created, """{"id":"created"}"""));
        var auth = new FakeTokenProvider { AccountId = "home-account", AccountName = "me@example.com" };
        auth.SilentErrors.Enqueue(null);
        auth.SilentErrors.Enqueue(new MicrosoftCalendarAuthenticationRequiredException("write consent required"));
        var provider = new MicrosoftCalendarProvider(auth,
            new GraphCalendarClient(new HttpClient(handler)), new ProtectedCalendarCache(directory));
        await provider.GetCalendarsAsync();
        var draft = new EventDraft("editable", "Create", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), false, "");

        await provider.CreateEventAsync(draft);

        Equal(1, auth.InteractiveCalls);
        Equal("Bearer interactive-token", handler.Requests[1].Authorization);
    }
    finally { DeleteDirectory(directory); }
}

static async Task OfflineCacheAsync()
{
    var directory = TestDirectory();
    try
    {
        var calendar = new CalendarSource("cal", "Calendar", "auto", true, "me@example.com");
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var cache = new ProtectedCalendarCache(directory);
        await cache.SaveAsync(new EventCacheQuery("home-account", ["cal"], start, end),
            [new CalendarEvent("cached", "cal", "Cached event", start.AddHours(8), start.AddHours(9), false, "")],
            new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero), default);
        var auth = new FakeTokenProvider { AccountId = "home-account", AccountName = "me@example.com" };
        var provider = new MicrosoftCalendarProvider(auth,
            new GraphCalendarClient(new HttpClient(new ThrowingHandler(new HttpRequestException("offline")))), cache);

        var events = await provider.GetEventsAsync([calendar], start, end);

        Equal(1, events.Count);
        Equal("Cached event", events[0].Subject);
        Equal(MicrosoftCalendarDataState.OfflineCache, provider.CacheStatus.State);
        Equal(new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero), provider.CacheStatus.RefreshedAt);
        Contains("offline", provider.CacheStatus.ErrorMessage!);
    }
    finally { DeleteDirectory(directory); }
}

static async Task OfflineCalendarCacheAsync()
{
    var directory = TestDirectory();
    try
    {
        var cache = new ProtectedCalendarCache(directory);
        await cache.SaveCalendarsAsync("home-account",
            [new CalendarSource("cached-cal", "Cached calendar", "#123456", true, "me@example.com")],
            new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero), default);
        var auth = new FakeTokenProvider { AccountId = "home-account", AccountName = "me@example.com" };
        var provider = new MicrosoftCalendarProvider(auth,
            new GraphCalendarClient(new HttpClient(new ThrowingHandler(new HttpRequestException("offline")))), cache);

        var calendars = await provider.GetCalendarsAsync();

        Equal(1, calendars.Count);
        Equal("Cached calendar", calendars[0].Name);
        Equal(MicrosoftCalendarDataState.OfflineCache, provider.CacheStatus.State);
        Equal(new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero), provider.CacheStatus.RefreshedAt);
    }
    finally { DeleteDirectory(directory); }
}

static async Task SignOutClearsCacheAsync()
{
    var directory = TestDirectory();
    try
    {
        var cache = new ProtectedCalendarCache(directory);
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        await cache.SaveAsync(new EventCacheQuery("home-account", [], start, start.AddDays(1)), [], start, default);
        var auth = new FakeTokenProvider { AccountId = "home-account", AccountName = "me@example.com" };
        var provider = new MicrosoftCalendarProvider(auth,
            new GraphCalendarClient(new HttpClient(new ScriptedHandler())), cache);

        await provider.SignOutAsync();

        Equal(false, File.Exists(cache.CachePath));
        Equal(1, auth.SignOutCalls);
        Equal(MicrosoftCalendarDataState.Empty, provider.CacheStatus.State);
    }
    finally { DeleteDirectory(directory); }
}

static async Task SignOutFailureStillClearsCacheAsync()
{
    var directory = TestDirectory();
    try
    {
        var cache = new ProtectedCalendarCache(directory);
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        await cache.SaveAsync(new EventCacheQuery("home-account", [], start, start.AddDays(1)), [], start, default);
        var auth = new FakeTokenProvider
        {
            AccountId = "home-account",
            AccountName = "me@example.com",
            SignOutError = new InvalidOperationException("WAM removal failed")
        };
        var provider = new MicrosoftCalendarProvider(auth,
            new GraphCalendarClient(new HttpClient(new ScriptedHandler())), cache);

        await ThrowsAsync<InvalidOperationException>(() => provider.SignOutAsync());

        Equal(false, File.Exists(cache.CachePath));
        Equal(MicrosoftCalendarDataState.Empty, provider.CacheStatus.State);
    }
    finally { DeleteDirectory(directory); }
}

static Task ClientIdValidation()
{
    var directory = TestDirectory();
    try
    {
        Throws<MicrosoftCalendarConfigurationException>(() =>
            new MicrosoftCalendarProvider(0, "replace-with-client-id", directory));
        Throws<ArgumentException>(() =>
            new MicrosoftCalendarProvider(1, Guid.NewGuid().ToString(), " "));
    }
    finally { DeleteDirectory(directory); }
    return Task.CompletedTask;
}

static async Task IcsRecurrenceAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "calendar.ics");
        await File.WriteAllTextAsync(path, """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Agenda tests//EN
            BEGIN:VTIMEZONE
            TZID:Europe/Stockholm
            X-LIC-LOCATION:Europe/Stockholm
            END:VTIMEZONE
            BEGIN:VEVENT
            UID:weekly-meeting
            SUMMARY:Weekly meeting
            LOCATION:Studio
            DTSTART;TZID=Europe/Stockholm:20260323T090000
            DTEND;TZID=Europe/Stockholm:20260323T100000
            RRULE:FREQ=WEEKLY;COUNT=3
            EXDATE;TZID=Europe/Stockholm:20260330T090000
            END:VEVENT
            END:VCALENDAR
            """.Replace("\r", "").Replace("\n", "\r\n"));
        var feed = new FeedConfiguration("team", "Team", path, "#336699");
        var provider = new IcsCalendarProvider([feed], directory);
        var calendars = await provider.GetCalendarsAsync();

        var events = await provider.GetEventsAsync(calendars,
            new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero));

        Equal(2, events.Count);
        Equal(new DateTimeOffset(2026, 3, 23, 9, 0, 0, TimeSpan.FromHours(1)), events[0].Start);
        Equal(new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.FromHours(2)), events[1].Start);
        Equal("Studio", events[0].Location);
        Equal(CalendarFeedDataState.Fresh, provider.FeedStatuses["team"].State);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsReadOnlyAsync()
{
    var directory = TestDirectory();
    try
    {
        var provider = new IcsCalendarProvider([], directory);
        var draft = new EventDraft("feed", "Write", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), false, "");

        await ThrowsAsync<CalendarReadOnlyException>(() => provider.CreateEventAsync(draft));
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsMultiDayAllDayAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "all-day.ics");
        await File.WriteAllTextAsync(path, """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Agenda tests//EN
            BEGIN:VEVENT
            UID:conference
            SUMMARY:Conference
            DTSTART;VALUE=DATE:20260909
            DTEND;VALUE=DATE:20260912
            END:VEVENT
            END:VCALENDAR
            """.Replace("\r", "").Replace("\n", "\r\n"));
        var provider = new IcsCalendarProvider(
            [new FeedConfiguration("local", "Local", path, "auto")], directory);
        var events = await provider.GetEventsAsync(await provider.GetCalendarsAsync(),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        Equal(true, events.Single().IsAllDay);
        Equal(new DateOnly(2026, 9, 9), events.Single().AllDayStart);
        Equal(new DateOnly(2026, 9, 12), events.Single().AllDayEnd);
    }
    finally { DeleteDirectory(directory); }
}

static async Task InvalidIcsRaisesErrorAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "invalid.ics");
        await File.WriteAllTextAsync(path, "This is not a calendar");
        var provider = new IcsCalendarProvider(
            [new FeedConfiguration("invalid", "Invalid", path, "auto")], directory);

        var error = await ThrowsAsync<CalendarFeedException>(() => provider.GetEventsAsync(
            provider.GetCalendarsAsync().GetAwaiter().GetResult(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)));

        Contains("could not be parsed", error.Message);
    }
    finally { DeleteDirectory(directory); }
}

static async Task FeedConfigurationEncryptionAsync()
{
    var directory = TestDirectory();
    try
    {
        var secretUrl = "https://calendar.google.com/calendar/ical/private-secret/basic.ics";
        await FeedConfigurationStore.SaveAsync(directory,
            [new FeedConfiguration("personal", "Personal", secretUrl, "#008000")]);

        var bytes = await File.ReadAllBytesAsync(FeedConfigurationStore.GetStoragePath(directory));
        DoesNotContain("private-secret", Encoding.UTF8.GetString(bytes));
        var loaded = await FeedConfigurationStore.LoadAsync(directory);
        Equal(secretUrl, loaded[0].Location);
        Equal("Personal", loaded[0].Name);
        var edited=loaded[0] with { Name="Renamed",Color="#B69CFF",Location="https://example.com/edited-private.ics" };
        await FeedConfigurationStore.SaveAsync(directory,[edited]);
        Equal(edited,(await FeedConfigurationStore.LoadAsync(directory)).Single());
        DoesNotContain("edited-private",Encoding.UTF8.GetString(await File.ReadAllBytesAsync(FeedConfigurationStore.GetStoragePath(directory))));
    }
    finally { DeleteDirectory(directory); }
}

static Task FeedLocationValidation()
{
    var directory = TestDirectory();
    try
    {
        Throws<ArgumentException>(() => new IcsCalendarProvider(
            [new FeedConfiguration("bad", "Bad", "http://example.com/calendar.ics", "auto")], directory));
        Throws<ArgumentException>(() => new IcsCalendarProvider(
            [new FeedConfiguration("bad", "Bad", "ftp://example.com/calendar.ics", "auto")], directory));
    }
    finally { DeleteDirectory(directory); }
    return Task.CompletedTask;
}

static async Task IcsOfflineCacheAsync()
{
    var directory = TestDirectory();
    try
    {
        const string url = "https://example.com/secret-feed.ics";
        const string calendarText = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Agenda tests//EN
            BEGIN:VEVENT
            UID:cached-event
            SUMMARY:Cached private event
            DTSTART:20260909T080000Z
            DTEND:20260909T090000Z
            END:VEVENT
            END:VCALENDAR
            """;
        var feed = new FeedConfiguration("remote", "Remote", url, "auto");
        var success = new IcsCalendarProvider([feed], directory,
            new HttpClient(new ScriptedHandler(Json(HttpStatusCode.OK, calendarText))));
        var calendars = await success.GetCalendarsAsync();
        await success.GetEventsAsync(calendars,
            new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));
        var cacheFile = Directory.GetFiles(directory, "calendar-feed-*.bin").Single();
        DoesNotContain("Cached private event", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(cacheFile)));

        var offline = new IcsCalendarProvider([feed], directory,
            new HttpClient(new ThrowingHandler(new HttpRequestException("network unavailable"))));
        var events = await offline.GetEventsAsync(calendars,
            new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));

        Equal("Cached private event", events.Single().Subject);
        Equal(CalendarFeedDataState.OfflineCache, offline.FeedStatuses["remote"].State);
        Contains("network unavailable", offline.FeedStatuses["remote"].ErrorMessage!);
        using var recolored=new IcsCalendarProvider([feed with { Name="Renamed",Color="#B69CFF" }],directory,
            new HttpClient(new ThrowingHandler(new HttpRequestException("offline"))));
        var recoloredCalendars=await recolored.GetCalendarsAsync();
        Equal("#B69CFF",recoloredCalendars.Single().Color);
        Equal("Renamed",recoloredCalendars.Single().Name);
        Equal(1,(await recolored.GetEventsAsync(recoloredCalendars,
            new DateTimeOffset(2026,9,9,0,0,0,TimeSpan.Zero),new DateTimeOffset(2026,9,10,0,0,0,TimeSpan.Zero))).Count);
        using var moved=new IcsCalendarProvider([feed with { Location="https://example.com/different-feed.ics" }],directory,
            new HttpClient(new ThrowingHandler(new HttpRequestException("offline"))));
        await ThrowsAsync<CalendarFeedException>(()=>moved.GetEventsAsync(calendars,
            new DateTimeOffset(2026,9,9,0,0,0,TimeSpan.Zero),new DateTimeOffset(2026,9,10,0,0,0,TimeSpan.Zero)));
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsRejectsDowngradeRedirectAsync()
{
    var directory = TestDirectory();
    try
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("http://example.com/plaintext.ics");
        var handler = new ScriptedHandler(redirect,
            Json(HttpStatusCode.OK, "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n"));
        var provider = new IcsCalendarProvider(
            [new FeedConfiguration("secure", "Secure", "https://example.com/calendar.ics", "auto")],
            directory, new HttpClient(handler));

        var error = await ThrowsAsync<CalendarFeedException>(() => provider.GetEventsAsync(
            provider.GetCalendarsAsync().GetAwaiter().GetResult(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)));

        Contains("non-HTTPS", error.ToString());
        Equal(1, handler.Requests.Count);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsPartialFailureAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "good.ics");
        await File.WriteAllTextAsync(path, TestIcs("UID:good\r\nSUMMARY:Good\r\nDTSTART:20260909T080000Z\r\nDTEND:20260909T090000Z"));
        const string secret = "https://example.com/private-secret/calendar.ics";
        using var provider = new IcsCalendarProvider(
            [new FeedConfiguration("bad", "Bad", secret, "auto"), new FeedConfiguration("good", "Good", path, "auto")],
            directory, new HttpClient(new ThrowingHandler(new HttpRequestException("Cannot GET " + secret))));
        var events = await provider.GetEventsAsync(await provider.GetCalendarsAsync(),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z"), DateTimeOffset.Parse("2026-09-10T00:00:00Z"));
        Equal("Good", events.Single().Subject);
        Equal(CalendarFeedDataState.Fresh, provider.FeedStatuses["good"].State);
        Equal(CalendarFeedDataState.Empty, provider.FeedStatuses["bad"].State);
        DoesNotContain("private-secret", provider.FeedStatuses["bad"].ErrorMessage!);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsOverridesAndOverlapAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "override.ics");
        await File.WriteAllTextAsync(path, TestIcs(
            "UID:recurring\r\nSUMMARY:Original\r\nDTSTART:20260908T080000Z\r\nDTEND:20260908T090000Z\r\nRRULE:FREQ=DAILY;COUNT=4",
            "UID:recurring\r\nRECURRENCE-ID:20260909T080000Z\r\nSUMMARY:Moved\r\nDTSTART:20260909T110000Z\r\nDTEND:20260909T120000Z",
            "UID:overlap\r\nSUMMARY:Conference\r\nDTSTART;VALUE=DATE:20260908\r\nDTEND;VALUE=DATE:20260911"));
        using var provider = new IcsCalendarProvider([new FeedConfiguration("feed", "Feed", path, "auto")], directory);
        var events = await provider.GetEventsAsync(await provider.GetCalendarsAsync(),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z"), DateTimeOffset.Parse("2026-09-10T00:00:00Z"));
        Equal(2, events.Count);
        Equal("Conference", events[0].Subject);
        Equal(new DateOnly(2026, 9, 8), events[0].AllDayStart);
        Equal("Moved", events[1].Subject);
        Equal(DateTimeOffset.Parse("2026-09-09T11:00:00Z"), events[1].Start);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsOccurrenceLimitAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "dense.ics");
        await File.WriteAllTextAsync(path, TestIcs(
            "UID:dense\r\nSUMMARY:Dense\r\nDTSTART:20260909T000000Z\r\nDTEND:20260909T000001Z\r\nRRULE:FREQ=SECONDLY;COUNT=60000"));
        using var provider = new IcsCalendarProvider([new FeedConfiguration("feed", "Feed", path, "auto")], directory);
        var error = await ThrowsAsync<CalendarFeedException>(() => provider.GetEventsAsync(
            provider.GetCalendarsAsync().GetAwaiter().GetResult(),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z"), DateTimeOffset.Parse("2026-09-10T00:00:00Z")));
        Contains("limit", error.Message);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsCancellationAsync()
{
    var directory = TestDirectory();
    try
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var provider = new IcsCalendarProvider(
            [new FeedConfiguration("feed", "Feed", "https://example.com/calendar.ics", "auto")], directory);
        await ThrowsAsync<OperationCanceledException>(() => provider.GetEventsAsync(
            provider.GetCalendarsAsync().GetAwaiter().GetResult(), DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1), cancellation.Token));
    }
    finally { DeleteDirectory(directory); }
}

static string TestIcs(params string[] events) =>
    "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Agenda tests//EN\r\n" +
    string.Join("", events.Select(value => "BEGIN:VEVENT\r\n" + value + "\r\nEND:VEVENT\r\n")) +
    "END:VCALENDAR\r\n";

static async Task IcsMalformedCacheAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "feed.ics");
        await File.WriteAllTextAsync(path, TestIcs("UID:good\r\nSUMMARY:Kept\r\nDTSTART:20260909T080000Z\r\nDTEND:20260909T090000Z"));
        using var provider = new IcsCalendarProvider([new FeedConfiguration("feed", "Feed", path, "auto")], directory);
        var calendars = await provider.GetCalendarsAsync();
        var start = DateTimeOffset.Parse("2026-09-09T00:00:00Z");
        await provider.GetEventsAsync(calendars, start, start.AddDays(1));
        await File.WriteAllTextAsync(path, "private-secret invalid calendar");
        var events = await provider.GetEventsAsync(calendars, start, start.AddDays(1));
        Equal("Kept", events.Single().Subject);
        Equal(CalendarFeedDataState.OfflineCache, provider.FeedStatuses["feed"].State);
        DoesNotContain("private-secret", provider.FeedStatuses["feed"].ErrorMessage!);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsByteLimitAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "large.ics");
        await File.WriteAllBytesAsync(path, new byte[5 * 1024 * 1024 + 1]);
        using var provider = new IcsCalendarProvider([new FeedConfiguration("feed", "Feed", path, "auto")], directory);
        var error = await ThrowsAsync<CalendarFeedException>(() => provider.GetEventsAsync(
            provider.GetCalendarsAsync().GetAwaiter().GetResult(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)));
        Contains("byte limit", error.Message);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsCancellationAfterDownloadAsync()
{
    var directory = TestDirectory();
    try
    {
        using var cancellation = new CancellationTokenSource();
        using var provider = new IcsCalendarProvider(
            [new FeedConfiguration("feed", "Feed", "https://example.com/calendar.ics", "auto")], directory,
            new HttpClient(new CancellingHandler(cancellation)));
        await ThrowsAsync<OperationCanceledException>(() => provider.GetEventsAsync(
            provider.GetCalendarsAsync().GetAwaiter().GetResult(), DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1), cancellation.Token));
    }
    finally { DeleteDirectory(directory); }
}

static async Task MicrosoftTimeoutCacheAsync()
{
    var directory = TestDirectory();
    try
    {
        var cache = new ProtectedCalendarCache(directory);
        var start = DateTimeOffset.Parse("2026-09-09T00:00:00Z");
        var calendar = new CalendarSource("cal", "Cached", "auto", true, "me@example.com");
        await cache.SaveCalendarsAsync("home-account", [calendar], start, default);
        await cache.SaveAsync(new EventCacheQuery("home-account", ["cal"], start, start.AddDays(1)),
            [new CalendarEvent("cached", "cal", "Cached", start.AddHours(8), start.AddHours(9), false, "")],
            start, default);
        var auth = new FakeTokenProvider { AccountId = "home-account", AccountName = "me@example.com" };
        using var provider = new MicrosoftCalendarProvider(auth,
            new GraphCalendarClient(new HttpClient(new ThrowingHandler(new TaskCanceledException("timeout")))), cache);
        Equal(1, (await provider.GetCalendarsAsync()).Count);
        Equal(MicrosoftCalendarDataState.OfflineCache, provider.CacheStatus.State);
        Equal(1, (await provider.GetEventsAsync([calendar], start, start.AddDays(1))).Count);
        Equal(MicrosoftCalendarDataState.OfflineCache, provider.CacheStatus.State);
        Equal(0, auth.InteractiveCalls);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsRejectsExruleAsync()
{
    var directory = TestDirectory();
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "exrule.ics");
        await File.WriteAllTextAsync(path, TestIcs(
            "UID:excluded\r\nSUMMARY:Excluded\r\nDTSTART:20260909T000000Z\r\nDTEND:20260909T010000Z\r\nRRULE:FREQ=DAILY;COUNT=2\r\nEXRULE:FREQ=DAILY;COUNT=2"));
        using var provider = new IcsCalendarProvider([new FeedConfiguration("feed", "Feed", path, "auto")], directory);
        var error = await ThrowsAsync<CalendarFeedException>(() => provider.GetEventsAsync(
            provider.GetCalendarsAsync().GetAwaiter().GetResult(),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z"), DateTimeOffset.Parse("2026-09-10T00:00:00Z")));
        Contains("EXRULE", error.Message);
    }
    finally { DeleteDirectory(directory); }
}

static async Task IcsBodyDeadlineAsync()
{
    var directory = TestDirectory();
    try
    {
        using var externalDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(23));
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
        using var provider = new IcsCalendarProvider(
            [new FeedConfiguration("feed", "Feed", "https://example.com/calendar.ics", "auto")], directory,
            new HttpClient(new ScriptedHandler(response)));
        var error = await ThrowsAsync<CalendarFeedException>(() => provider.GetEventsAsync(
            provider.GetCalendarsAsync().GetAwaiter().GetResult(), DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1), externalDeadline.Token));
        Contains("timed out", error.Message);
        Equal(false, externalDeadline.IsCancellationRequested);
    }
    finally { DeleteDirectory(directory); }
}

static string TestDirectory() => Path.Combine(Path.GetTempPath(), "agenda-provider-tests", Guid.NewGuid().ToString("N"));

static void DeleteDirectory(string directory)
{
    if (Directory.Exists(directory)) Directory.Delete(directory, true);
}

static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json")
};

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
}

static void DoesNotContain(string expected, string actual)
{
    if (actual.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Did not expect '{actual}' to contain '{expected}'.");
}

static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T exception) { return exception; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static T Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T exception) { return exception; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

sealed class ScriptedHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!,
            request.Headers.Authorization?.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        if (_responses.Count == 0) throw new InvalidOperationException("No scripted response remains.");
        var response = _responses.Dequeue();
        response.RequestMessage = request;
        return response;
    }
}

sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(exception);
}

sealed class CancellingHandler(CancellationTokenSource cancellation) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellation.Cancel();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n")
        });
    }
}

sealed class StalledStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
}

sealed class FakeTokenProvider : IAccessTokenProvider
{
    public string? AccountName { get; set; }
    public string? AccountId { get; set; }
    public Exception? SilentError { get; set; }
    public Queue<Exception?> SilentErrors { get; } = [];
    public Exception? SignOutError { get; set; }
    public int SilentCalls { get; private set; }
    public int InteractiveCalls { get; private set; }
    public int SignOutCalls { get; private set; }

    public Task<string> AcquireTokenSilentAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        SilentCalls++;
        var error = SilentErrors.Count > 0 ? SilentErrors.Dequeue() : SilentError;
        return error is null ? Task.FromResult("silent-token") : Task.FromException<string>(error);
    }

    public Task<string> AcquireTokenInteractiveAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        InteractiveCalls++;
        return Task.FromResult("interactive-token");
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        SignOutCalls++;
        if (SignOutError is not null) return Task.FromException(SignOutError);
        AccountName = null;
        AccountId = null;
        return Task.CompletedTask;
    }
}
