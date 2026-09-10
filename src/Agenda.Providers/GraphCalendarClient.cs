using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Agenda.Core;

namespace Agenda.Providers;

internal sealed class GraphCalendarClient
{
    private const int MaximumPages = 100;
    private static readonly Uri GraphRoot = new("https://graph.microsoft.com/v1.0/");
    private readonly HttpClient _httpClient;

    public GraphCalendarClient(HttpClient httpClient) =>
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<IReadOnlyList<CalendarSource>> GetCalendarsAsync(
        string accessToken, string accountName, CancellationToken cancellationToken)
    {
        RequireToken(accessToken);
        var calendars = new List<CalendarSource>();
        var next = new Uri(GraphRoot,
            "me/calendars?$select=id,name,color,hexColor,canEdit,owner&$top=100");

        await ForEachPageAsync(next, accessToken, cancellationToken, root =>
        {
            foreach (var item in GetValueArray(root))
            {
                var id = RequiredString(item, "id");
                var name = OptionalString(item, "name") ?? "Calendar";
                var hexColor = OptionalString(item, "hexColor");
                var color = string.IsNullOrWhiteSpace(hexColor)
                    ? OptionalString(item, "color") ?? "auto"
                    : hexColor;
                var canEdit = item.TryGetProperty("canEdit", out var canEditElement) &&
                    canEditElement.ValueKind == JsonValueKind.True;
                var owner = accountName;
                if (item.TryGetProperty("owner", out var ownerElement) &&
                    ownerElement.ValueKind == JsonValueKind.Object)
                    owner = OptionalString(ownerElement, "address") ?? accountName;

                calendars.Add(new CalendarSource(id, name, color, canEdit, owner));
            }
        }).ConfigureAwait(false);

        return calendars;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        string accessToken, IReadOnlyList<CalendarSource> calendars, DateTimeOffset start,
        DateTimeOffset end, CancellationToken cancellationToken)
    {
        RequireToken(accessToken);
        ArgumentNullException.ThrowIfNull(calendars);
        if (end <= start) throw new ArgumentException("The event range end must be after its start.", nameof(end));

        var events = new List<CalendarEvent>();
        foreach (var calendar in calendars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Uri.EscapeDataString(calendar.Id);
            var startValue = Uri.EscapeDataString(start.ToString("O", CultureInfo.InvariantCulture));
            var endValue = Uri.EscapeDataString(end.ToString("O", CultureInfo.InvariantCulture));
            var path = $"me/calendars/{id}/calendarView?startDateTime={startValue}&endDateTime={endValue}" +
                "&$select=id,subject,start,end,isAllDay,location,webLink,onlineMeeting&$top=100";

            await ForEachPageAsync(new Uri(GraphRoot, path), accessToken, cancellationToken, root =>
            {
                foreach (var item in GetValueArray(root))
                    events.Add(ParseEvent(item, calendar.Id));
            }).ConfigureAwait(false);
        }

        events.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return events;
    }

    public async Task CreateEventAsync(string accessToken, CalendarSource calendar,
        EventDraft draft, CancellationToken cancellationToken)
    {
        RequireToken(accessToken);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(draft);
        if (!calendar.CanEdit)
            throw new CalendarReadOnlyException($"Calendar '{calendar.Name}' is read-only.");
        if (!string.Equals(calendar.Id, draft.CalendarId, StringComparison.Ordinal))
            throw new ArgumentException("The draft calendar does not match the target calendar.", nameof(draft));
        if (string.IsNullOrWhiteSpace(draft.Subject))
            throw new ArgumentException("An event subject is required.", nameof(draft));
        if (draft.End <= draft.Start)
            throw new ArgumentException("The event end must be after its start.", nameof(draft));

        var body = new Dictionary<string, object?>
        {
            ["subject"] = draft.Subject.Trim(),
            ["start"] = GraphDateTime(draft.Start, draft.IsAllDay),
            ["end"] = GraphDateTime(draft.End, draft.IsAllDay),
            ["isAllDay"] = draft.IsAllDay,
            ["location"] = new { displayName = draft.Location ?? string.Empty },
        };
        if (!string.IsNullOrWhiteSpace(draft.Notes))
            body["body"] = new { contentType = "text", content = draft.Notes };

        var uri = new Uri(GraphRoot, $"me/calendars/{Uri.EscapeDataString(calendar.Id)}/events");
        using var request = CreateRequest(HttpMethod.Post, uri, accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await ThrowGraphErrorAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task ForEachPageAsync(Uri firstPage, string accessToken,
        CancellationToken cancellationToken, Action<JsonElement> consume)
    {
        Uri? next = firstPage;
        for (var page = 0; next is not null; page++)
        {
            if (page >= MaximumPages)
                throw new GraphProtocolException($"Graph returned more than {MaximumPages} pages.");
            ValidateGraphUri(next);

            using var request = CreateRequest(HttpMethod.Get, next, accessToken);
            request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await ThrowGraphErrorAsync(response, cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            consume(document.RootElement);
            next = ReadNextLink(document.RootElement);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task ThrowGraphErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var message = response.ReasonPhrase ?? "Microsoft Graph request failed.";
        var code = "GraphError";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                code = OptionalString(error, "code") ?? code;
                message = OptionalString(error, "message") ?? message;
            }
        }
        catch (JsonException)
        {
            // Preserve the HTTP status and reason phrase when Graph returns a non-JSON proxy response.
        }

        throw new GraphApiException(response.StatusCode, code, message);
    }

    private static CalendarEvent ParseEvent(JsonElement item, string calendarId)
    {
        var startElement = item.GetProperty("start");
        var endElement = item.GetProperty("end");
        var allDay = item.TryGetProperty("isAllDay", out var allDayElement) && allDayElement.GetBoolean();
        var location = item.TryGetProperty("location", out var locationElement) &&
            locationElement.ValueKind == JsonValueKind.Object
            ? OptionalString(locationElement, "displayName") ?? string.Empty
            : string.Empty;
        string? joinUrl = null;
        if (item.TryGetProperty("onlineMeeting", out var onlineMeeting) &&
            onlineMeeting.ValueKind == JsonValueKind.Object)
            joinUrl = OptionalString(onlineMeeting, "joinUrl");

        var startText = RequiredString(startElement, "dateTime");
        var endText = RequiredString(endElement, "dateTime");
        return new CalendarEvent(
            RequiredString(item, "id"),
            calendarId,
            OptionalString(item, "subject") ?? "(No title)",
            ParseGraphDateTime(startText, OptionalString(startElement, "timeZone")),
            ParseGraphDateTime(endText, OptionalString(endElement, "timeZone")),
            allDay,
            location,
            OptionalString(item, "webLink"),
            joinUrl,
            allDay ? ParseDateOnly(startText) : null,
            allDay ? ParseDateOnly(endText) : null);
    }

    private static object GraphDateTime(DateTimeOffset value, bool allDay)
    {
        if (allDay)
            return new { dateTime = value.Date.ToString("yyyy-MM-dd'T'00:00:00.0000000", CultureInfo.InvariantCulture), timeZone = TimeZoneInfo.Local.Id };

        return new
        {
            dateTime = value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            timeZone = "UTC"
        };
    }

    private static DateTimeOffset ParseGraphDateTime(string dateTime, string? timeZone)
    {
        if (DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var withOffset) &&
            (dateTime.EndsWith('Z') || HasExplicitOffset(dateTime)))
            return withOffset;

        if (!DateTime.TryParse(dateTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var local))
            throw new GraphProtocolException($"Graph returned an invalid dateTime value: '{dateTime}'.");
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new GraphProtocolException($"Graph returned an unknown time zone: '{timeZone}'.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new GraphProtocolException($"Graph returned an invalid time zone: '{timeZone}'.", exception);
        }
    }

    private static bool HasExplicitOffset(string value)
    {
        var time = value.IndexOf('T');
        if (time < 0) return false;
        return value.IndexOf('+', time) >= 0 || value.IndexOf('-', time) >= 0;
    }

    private static DateOnly ParseDateOnly(string value)
    {
        if (value.Length >= 10 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        throw new GraphProtocolException($"Graph returned an invalid all-day date: '{value}'.");
    }

    private static JsonElement.ArrayEnumerator GetValueArray(JsonElement root)
    {
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            throw new GraphProtocolException("Graph response did not contain a value array.");
        return value.EnumerateArray();
    }

    private static string RequiredString(JsonElement element, string property)
    {
        var value = OptionalString(element, property);
        if (string.IsNullOrWhiteSpace(value))
            throw new GraphProtocolException($"Graph response omitted required property '{property}'.");
        return value;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Uri? ReadNextLink(JsonElement root)
    {
        var text = OptionalString(root, "@odata.nextLink");
        if (text is null) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            throw new GraphProtocolException("Graph returned an invalid paging nextLink.");
        ValidateGraphUri(uri);
        return uri;
    }

    private static void ValidateGraphUri(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(GraphRoot.Host, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort)
            throw new GraphProtocolException("Graph paging URI must use HTTPS on the graph.microsoft.com host.");
    }

    private static void RequireToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("An access token is required.", nameof(accessToken));
    }
}
