using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agenda.Core;

namespace Agenda.Providers;

internal sealed class ProtectedCalendarCache
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Agenda.MicrosoftCalendarCache.v1");
    private readonly string _directory;

    public ProtectedCalendarCache(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("A cache storage directory is required.", nameof(storageDirectory));
        _directory = Path.GetFullPath(storageDirectory);
    }

    public string CachePath => Path.Combine(_directory, "microsoft-calendar-cache.bin");
    public string CalendarCachePath => Path.Combine(_directory, "microsoft-calendars.bin");

    public Task SaveCalendarsAsync(string accountId, IReadOnlyList<CalendarSource> calendars,
        DateTimeOffset refreshedAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("An account ID is required.", nameof(accountId));
        ArgumentNullException.ThrowIfNull(calendars);
        return ProtectedJsonFile.WriteAsync(CalendarCachePath,
            new CalendarCacheSnapshot(accountId, refreshedAt, calendars.ToArray()),
            "Agenda.MicrosoftCalendarList.v1", cancellationToken);
    }

    public async Task<CalendarCacheSnapshot?> TryLoadCalendarsAsync(string accountId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(CalendarCachePath)) return null;
        var snapshot = await ProtectedJsonFile.ReadAsync<CalendarCacheSnapshot>(CalendarCachePath,
            "Agenda.MicrosoftCalendarList.v1", cancellationToken).ConfigureAwait(false);
        return snapshot is not null && string.Equals(snapshot.AccountId, accountId, StringComparison.Ordinal)
            ? snapshot
            : null;
    }

    public async Task SaveAsync(EventCacheQuery query, IReadOnlyList<CalendarEvent> events,
        DateTimeOffset refreshedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(events);
        Directory.CreateDirectory(_directory);
        var envelope = CacheEnvelope.From(query, events, refreshedAt);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(envelope);
        try
        {
            var protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            var temporaryPath = CachePath + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, CachePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<EventCacheSnapshot?> TryLoadAsync(EventCacheQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!File.Exists(CachePath)) return null;

        var protectedBytes = await File.ReadAllBytesAsync(CachePath, cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(plaintext)
                ?? throw new InvalidDataException("The Microsoft calendar cache is empty.");
            if (!envelope.Matches(query)) return null;
            return new EventCacheSnapshot(envelope.RefreshedAt, envelope.Events);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The Microsoft calendar cache is invalid.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Clear()
    {
        if (File.Exists(CachePath)) File.Delete(CachePath);
        if (File.Exists(CalendarCachePath)) File.Delete(CalendarCachePath);
        var temporaryPath = CachePath + ".tmp";
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
    }

    private sealed record CacheEnvelope(
        int Version,
        string AccountId,
        string[] CalendarIds,
        DateTimeOffset Start,
        DateTimeOffset End,
        DateTimeOffset RefreshedAt,
        CalendarEvent[] Events)
    {
        public static CacheEnvelope From(EventCacheQuery query, IReadOnlyList<CalendarEvent> events,
            DateTimeOffset refreshedAt) => new(1, query.AccountId,
            query.CalendarIds.Order(StringComparer.Ordinal).ToArray(), query.Start, query.End,
            refreshedAt, events.ToArray());

        public bool Matches(EventCacheQuery query) =>
            Version == 1 &&
            string.Equals(AccountId, query.AccountId, StringComparison.Ordinal) &&
            Start.Equals(query.Start) && End.Equals(query.End) &&
            CalendarIds.SequenceEqual(query.CalendarIds.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }
}
