using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agenda.Core;
using Ical.Net;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using Ical.Net.Utility;
using IcalCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace Agenda.Providers;

public sealed record FeedConfiguration(string Id, string Name, string Location, string Color);

public enum CalendarFeedDataState { Empty, Fresh, OfflineCache }

public sealed record CalendarFeedStatus(
    CalendarFeedDataState State,
    DateTimeOffset? RefreshedAt,
    string? ErrorMessage);

public sealed class CalendarFeedException(string feedName, string message, Exception? innerException = null)
    : InvalidOperationException($"Calendar feed '{feedName}': {message}", innerException);

public static class FeedConfigurationStore
{
    private const string FileName = "calendar-feeds.bin";
    private const string Purpose = "Agenda.FeedConfiguration.v1";

    public static string GetStoragePath(string storageDirectory) =>
        Path.Combine(RequireStorageDirectory(storageDirectory), FileName);

    public static async Task SaveAsync(string storageDirectory, IReadOnlyList<FeedConfiguration> feeds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        foreach (var feed in feeds) FeedConfigurationValidator.Validate(feed);
        await ProtectedJsonFile.WriteAsync(GetStoragePath(storageDirectory), feeds.ToArray(), Purpose,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<FeedConfiguration>> LoadAsync(string storageDirectory,
        CancellationToken cancellationToken = default)
    {
        var path = GetStoragePath(storageDirectory);
        if (!File.Exists(path)) return [];
        var feeds = await ProtectedJsonFile.ReadAsync<FeedConfiguration[]>(path, Purpose, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The feed configuration store is empty.");
        foreach (var feed in feeds) FeedConfigurationValidator.Validate(feed);
        return feeds;
    }

    private static string RequireStorageDirectory(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("A feed storage directory is required.", nameof(storageDirectory));
        return Path.GetFullPath(storageDirectory);
    }
}

public sealed class IcsCalendarProvider : ICalendarProvider, IDisposable
{
    private const int MaximumFeedBytes = 5 * 1024 * 1024;
    private const int MaximumOccurrences = 50_000;
    private readonly Dictionary<string, FeedConfiguration> _feeds;
    private readonly Dictionary<string, CalendarFeedStatus> _statuses;
    private readonly ProtectedFeedCache _cache;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public IcsCalendarProvider(IEnumerable<FeedConfiguration> feeds, string storageDirectory)
        : this(feeds, storageDirectory, CreateHttpClient(), true) { }

    internal IcsCalendarProvider(IEnumerable<FeedConfiguration> feeds, string storageDirectory,
        HttpClient httpClient, bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(feeds);
        _feeds = new Dictionary<string, FeedConfiguration>(StringComparer.Ordinal);
        foreach (var feed in feeds)
        {
            FeedConfigurationValidator.Validate(feed);
            if (!_feeds.TryAdd(feed.Id, feed))
                throw new ArgumentException($"Feed ID '{feed.Id}' is duplicated.", nameof(feeds));
        }
        _statuses = _feeds.Keys.ToDictionary(id => id,
            _ => new CalendarFeedStatus(CalendarFeedDataState.Empty, null, null), StringComparer.Ordinal);
        _cache = new ProtectedFeedCache(storageDirectory);
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    public string? AccountName => _feeds.Count == 0 ? null : "Calendar feeds";
    public IReadOnlyDictionary<string, CalendarFeedStatus> FeedStatuses => _statuses;

    public Task SignInAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<CalendarSource>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<CalendarSource> calendars = _feeds.Values.Select(feed =>
            new CalendarSource(feed.Id, feed.Name, feed.Color, false, "Calendar feeds")).ToArray();
        return Task.FromResult(calendars);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        IReadOnlyList<CalendarSource> calendars, DateTimeOffset start, DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        if (end <= start) throw new ArgumentException("The event range end must be after its start.", nameof(end));

        var events = new List<CalendarEvent>();
        var usableFeeds = 0;
        CalendarFeedException? firstFailure = null;
        foreach (var calendar in calendars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_feeds.TryGetValue(calendar.Id, out var feed))
                throw new ArgumentException($"Calendar feed '{calendar.Id}' is not configured.", nameof(calendars));

            try
            {
                var text = await ReadFeedAsync(feed, cancellationToken).ConfigureAwait(false);
                var parsed = ParseEvents(feed, text, start, end, cancellationToken);
                var refreshedAt = DateTimeOffset.UtcNow;
                await _cache.SaveAsync(feed.Id, feed.Location, text, refreshedAt, cancellationToken).ConfigureAwait(false);
                _statuses[feed.Id] = new CalendarFeedStatus(CalendarFeedDataState.Fresh, refreshedAt, null);
                events.AddRange(parsed);
                usableFeeds++;
            }
            catch (Exception exception) when (IsRefreshFailure(exception, cancellationToken))
            {
                var message = SafeRefreshError(exception);
                _statuses[feed.Id] = new CalendarFeedStatus(CalendarFeedDataState.Empty, null, message);
                firstFailure ??= new CalendarFeedException(feed.Name, message);
                try
                {
                    var cached = await _cache.TryLoadAsync(feed.Id, cancellationToken).ConfigureAwait(false);
                    // A renamed/recolored feed can reuse events; a different source cannot.
                    // Legacy cache entries without source identity require one successful refresh.
                    if (cached is null || !string.Equals(cached.Location,feed.Location,StringComparison.Ordinal)) continue;
                    var parsed = ParseEvents(feed, cached.Text, start, end, cancellationToken);
                    _statuses[feed.Id] = new CalendarFeedStatus(
                        CalendarFeedDataState.OfflineCache, cached.RefreshedAt, message);
                    events.AddRange(parsed);
                    usableFeeds++;
                }
                catch (Exception cacheException) when (IsRefreshFailure(cacheException, cancellationToken))
                {
                    _statuses[feed.Id] = new CalendarFeedStatus(CalendarFeedDataState.Empty, null,
                        message + " The offline cache is unavailable.");
                }
            }
        }

        if (usableFeeds == 0 && firstFailure is not null) throw firstFailure;
        events.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return events;
    }

    public Task CreateEventAsync(EventDraft draft, CancellationToken cancellationToken = default) =>
        Task.FromException(new CalendarReadOnlyException("ICS calendar feeds are read-only."));

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cache.Clear();
        foreach (var id in _statuses.Keys.ToArray())
            _statuses[id] = new CalendarFeedStatus(CalendarFeedDataState.Empty, null, null);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    private async Task<string> ReadFeedAsync(FeedConfiguration feed, CancellationToken cancellationToken)
    {
        // HttpClient's timeout ends at the headers with ResponseHeadersRead. Keep a
        // separate deadline alive through redirects and the entire body/file read.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        cancellationToken = deadline.Token;
        if (Uri.TryCreate(feed.Location, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            using var response = await SendHttpsAsync(feed, uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new CalendarFeedHttpException(feed.Name, response.StatusCode);
            if (response.Content.Headers.ContentLength > MaximumFeedBytes)
                throw new CalendarFeedException(feed.Name, $"content exceeds the {MaximumFeedBytes} byte limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return DecodeUtf8(await ReadBoundedAsync(stream, cancellationToken).ConfigureAwait(false), feed.Name);
        }

        var path = Path.GetFullPath(feed.Location);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException($"Calendar feed file '{path}' was not found.", path);
        if (info.Length > MaximumFeedBytes)
            throw new CalendarFeedException(feed.Name, $"content exceeds the {MaximumFeedBytes} byte limit.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return DecodeUtf8(await ReadBoundedAsync(file, cancellationToken).ConfigureAwait(false), feed.Name);
    }

    private async Task<HttpResponseMessage> SendHttpsAsync(FeedConfiguration feed, Uri firstUri,
        CancellationToken cancellationToken)
    {
        var current = firstUri;
        for (var redirectCount = 0; redirectCount <= 5; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd("text/calendar, text/plain;q=0.9, */*;q=0.1");
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode)) return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (redirectCount == 5)
                throw new CalendarFeedException(feed.Name, "more than 5 redirects were returned.");
            if (location is null)
                throw new CalendarFeedException(feed.Name, "a redirect response omitted its Location header.");
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (!current.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new CalendarFeedException(feed.Name, "an HTTPS request redirected to a non-HTTPS location.");
        }
        throw new CalendarFeedException(feed.Name, "the redirect limit was exceeded.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static IReadOnlyList<CalendarEvent> ParseEvents(FeedConfiguration feed, string text,
        DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRecurrenceComplexity(feed, text);
            var timer = Stopwatch.StartNew();
            var calendars = CalendarCollection.Load(text);
            if (calendars.Count == 0)
                throw new CalendarFeedException(feed.Name, "content contains no VCALENDAR.");
            var rangeEnd = new CalDateTime(end.UtcDateTime);
            var events = new List<CalendarEvent>();
            var count = 0;
            // Start from the series origin: this also bounds historical COUNT evaluation and
            // retains events that begin before the query but overlap its start.
            var options = new EvaluationOptions { MaxUnmatchedIncrementsLimit = 1_000 };
            foreach (var occurrence in calendars.GetOccurrences<IcalCalendarEvent>(null, options)
                         .TakeWhileBefore(rangeEnd))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++count > MaximumOccurrences || timer.Elapsed > TimeSpan.FromSeconds(5))
                    throw new CalendarFeedException(feed.Name, "recurrence evaluation exceeded its safety limit.");
                var calendarEvent = MapOccurrence(feed, occurrence);
                if (calendarEvent.End > start && calendarEvent.Start < end &&
                    occurrence.Source is IcalCalendarEvent source &&
                    !string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                    events.Add(calendarEvent);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return events;
        }
        catch (OperationCanceledException) { throw; }
        catch (EvaluationLimitExceededException)
        {
            throw new CalendarFeedException(feed.Name, "recurrence evaluation exceeded its safety limit.");
        }
        catch (CalendarFeedException) { throw; }
        catch (Exception exception)
        {
            throw new CalendarFeedException(feed.Name,
                "content could not be parsed as a supported iCalendar feed.", exception);
        }
    }

    private static CalendarEvent MapOccurrence(FeedConfiguration feed, Occurrence occurrence)
    {
        if (occurrence.Source is not IcalCalendarEvent source)
            throw new CalendarFeedException(feed.Name, "an occurrence was not a VEVENT.");
        var startValue = occurrence.Period.StartTime;
        var endValue = occurrence.Period.EffectiveEndTime ?? startValue;
        var start = ToDateTimeOffset(startValue);
        var end = ToDateTimeOffset(endValue);
        var allDay = !startValue.HasTime;
        var uid = string.IsNullOrWhiteSpace(source.Uid) ? "event" : source.Uid;
        var id = $"{feed.Id}:{uid}:{start.UtcTicks}";
        return new CalendarEvent(id, feed.Id, source.Summary ?? "(No title)", start, end, allDay,
            source.Location ?? string.Empty, source.Url?.ToString(), null,
            allDay ? startValue.Date : null, allDay ? endValue.Date : null);
    }

    private static DateTimeOffset ToDateTimeOffset(CalDateTime value)
    {
        if (!value.HasTime || value.IsFloating)
            return new DateTimeOffset(value.Value, TimeZoneInfo.Local.GetUtcOffset(value.Value));
        var offset = value.Value - value.AsUtc;
        return new DateTimeOffset(value.Value, offset);
    }

    private static bool IsRefreshFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException or CalendarFeedException or
            UnauthorizedAccessException or CryptographicException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static string SafeRefreshError(Exception exception) => exception switch
    {
        CalendarFeedException => exception.Message,
        HttpRequestException { StatusCode: not null } http => $"server returned HTTP {(int)http.StatusCode.Value}.",
        HttpRequestException => "network unavailable; the feed could not be downloaded.",
        OperationCanceledException => "the feed request timed out.",
        _ => "the feed or its protected cache could not be read."
    };

    private static void ValidateRecurrenceComplexity(FeedConfiguration feed, string text)
    {
        // Bound candidate sets that Ical.Net may materialize internally before yielding.
        // Unfolding is required because providers routinely fold long RRULE lines.
        var unfolded = text.Replace("\r\n ", "").Replace("\r\n\t", "")
            .Replace("\n ", "").Replace("\n\t", "");
        foreach (var line in unfolded.Split('\n'))
        {
            if (line.StartsWith("EXRULE:", StringComparison.OrdinalIgnoreCase))
                throw new CalendarFeedException(feed.Name, "obsolete EXRULE exclusions are unsupported; use EXDATE.");
            if (!line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)) continue;
            long combinations = 1;
            foreach (var part in line[(line.IndexOf(':') + 1)..].Split(';'))
            {
                if (!part.StartsWith("BY", StringComparison.OrdinalIgnoreCase) ||
                    part.StartsWith("BYSETPOS=", StringComparison.OrdinalIgnoreCase)) continue;
                combinations *= part.Count(character => character == ',') + 1L;
                if (combinations > MaximumOccurrences)
                    throw new CalendarFeedException(feed.Name, "recurrence candidates exceed the safety limit.");
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) return buffer.ToArray();
            if (buffer.Length + read > MaximumFeedBytes)
                throw new InvalidDataException($"Calendar feed exceeds the {MaximumFeedBytes} byte limit.");
            buffer.Write(chunk, 0, read);
        }
    }

    private static string DecodeUtf8(byte[] bytes, string feedName)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException exception)
        {
            throw new CalendarFeedException(feedName, "content is not valid UTF-8.", exception);
        }
    }
}

internal sealed class CalendarFeedHttpException(string feedName, HttpStatusCode statusCode)
    : HttpRequestException($"Calendar feed '{feedName}' returned HTTP {(int)statusCode}.", null, statusCode);

internal static class FeedConfigurationValidator
{
    public static void Validate(FeedConfiguration feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (string.IsNullOrWhiteSpace(feed.Id)) throw new ArgumentException("A feed ID is required.", nameof(feed));
        if (string.IsNullOrWhiteSpace(feed.Name)) throw new ArgumentException("A feed name is required.", nameof(feed));
        if (string.IsNullOrWhiteSpace(feed.Location)) throw new ArgumentException("A feed location is required.", nameof(feed));
        if (Path.IsPathFullyQualified(feed.Location))
        {
            // Absolute Windows paths also parse as URIs with a drive-letter scheme.
        }
        else if (Uri.TryCreate(feed.Location, UriKind.Absolute, out var uri))
        {
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Network calendar feeds must use HTTPS.", nameof(feed));
        }
        else
            throw new ArgumentException("A feed location must be an HTTPS URL or an absolute local file path.", nameof(feed));
    }
}

internal sealed class ProtectedFeedCache
{
    private const string Purpose = "Agenda.IcsFeedCache.v1";
    private readonly string _directory;

    public ProtectedFeedCache(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("A feed cache directory is required.", nameof(storageDirectory));
        _directory = Path.GetFullPath(storageDirectory);
    }

    public Task SaveAsync(string feedId, string location, string text, DateTimeOffset refreshedAt,
        CancellationToken cancellationToken) => ProtectedJsonFile.WriteAsync(
        PathFor(feedId), new FeedCacheEnvelope(refreshedAt, text, location), PurposeFor(feedId), cancellationToken);

    public Task<FeedCacheEnvelope?> TryLoadAsync(string feedId, CancellationToken cancellationToken)
    {
        var path = PathFor(feedId);
        return File.Exists(path)
            ? ProtectedJsonFile.ReadAsync<FeedCacheEnvelope>(path, PurposeFor(feedId), cancellationToken)
            : Task.FromResult<FeedCacheEnvelope?>(null);
    }

    public void Clear()
    {
        if (!Directory.Exists(_directory)) return;
        foreach (var path in Directory.EnumerateFiles(_directory, "calendar-feed-*.bin", SearchOption.TopDirectoryOnly))
            File.Delete(path);
    }

    private string PathFor(string feedId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(feedId)));
        return Path.Combine(_directory, $"calendar-feed-{hash}.bin");
    }

    private static string PurposeFor(string feedId) => $"{Purpose}:{feedId}";
}

internal sealed record FeedCacheEnvelope(DateTimeOffset RefreshedAt, string Text, string? Location = null);

internal static class ProtectedJsonFile
{
    public static async Task WriteAsync<T>(string path, T value, string purpose,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = ProtectedData.Protect(plaintext, Encoding.UTF8.GetBytes(purpose), DataProtectionScope.CurrentUser);
            var temporaryPath = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, path, true);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public static async Task<T?> ReadAsync<T>(string path, string purpose, CancellationToken cancellationToken)
    {
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(protectedBytes, Encoding.UTF8.GetBytes(purpose), DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plaintext);
        }
        catch (JsonException exception) { throw new InvalidDataException("Protected calendar data is invalid.", exception); }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
