using Agenda.Core;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace Agenda.Providers;

public enum MicrosoftCalendarDataState
{
    Empty,
    Fresh,
    OfflineCache,
}

public sealed record MicrosoftCalendarCacheStatus(
    MicrosoftCalendarDataState State,
    DateTimeOffset? RefreshedAt,
    string? ErrorMessage);

public sealed class MicrosoftCalendarConfigurationException(string message) : ArgumentException(message);
public sealed class MicrosoftCalendarAuthenticationRequiredException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

internal interface IAccessTokenProvider
{
    string? AccountName { get; }
    string? AccountId { get; }
    Task<string> AcquireTokenSilentAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken);
    Task<string> AcquireTokenInteractiveAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
}

public sealed class MicrosoftCalendarProvider : ICalendarProvider, IDisposable
{
    private static readonly string[] ReadScopes = ["Calendars.Read"];
    private static readonly string[] WriteScopes = ["Calendars.ReadWrite"];
    private readonly IAccessTokenProvider _authentication;
    private readonly GraphCalendarClient _graph;
    private readonly ProtectedCalendarCache _cache;
    private readonly Dictionary<string, CalendarSource> _knownCalendars = new(StringComparer.Ordinal);
    private readonly HttpClient? _ownedHttpClient;

    public MicrosoftCalendarProvider(nint parentWindow, string clientId, string storageDirectory)
    {
        if (!Guid.TryParse(clientId, out var parsedClientId) || parsedClientId == Guid.Empty)
            throw new MicrosoftCalendarConfigurationException(
                "A valid Microsoft Entra application client ID is required. See docs/microsoft-account-setup.md.");
        if (parentWindow == 0)
            throw new ArgumentException("A non-zero parent window handle is required for the Windows account picker.", nameof(parentWindow));
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("A cache storage directory is required.", nameof(storageDirectory));

        var brokerOptions = new BrokerOptions(BrokerOptions.OperatingSystems.Windows)
        {
            Title = "Agenda"
        };
        var application = PublicClientApplicationBuilder.Create(parsedClientId.ToString())
            .WithAuthority(AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
            .WithDefaultRedirectUri()
            .WithParentActivityOrWindow(() => (IntPtr)parentWindow)
            .WithBroker(brokerOptions)
            .Build();
        _authentication = new MsalAccessTokenProvider(application);
        _ownedHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _graph = new GraphCalendarClient(_ownedHttpClient);
        _cache = new ProtectedCalendarCache(storageDirectory);
    }

    internal MicrosoftCalendarProvider(IAccessTokenProvider authentication,
        GraphCalendarClient graph, ProtectedCalendarCache cache)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public string? AccountName => _authentication.AccountName;
    public MicrosoftCalendarCacheStatus CacheStatus { get; private set; } =
        new(MicrosoftCalendarDataState.Empty, null, null);

    public async Task SignInAsync(CancellationToken cancellationToken = default) =>
        _ = await _authentication.AcquireTokenInteractiveAsync(ReadScopes, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<CalendarSource>> GetCalendarsAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await _authentication.AcquireTokenSilentAsync(ReadScopes, cancellationToken)
            .ConfigureAwait(false);
        var accountId = RequireAccountId();
        IReadOnlyList<CalendarSource> calendars;
        try
        {
            calendars = await _graph.GetCalendarsAsync(token, RequireAccountName(), cancellationToken)
                .ConfigureAwait(false);
            var refreshedAt = DateTimeOffset.UtcNow;
            await _cache.SaveCalendarsAsync(accountId, calendars, refreshedAt, cancellationToken)
                .ConfigureAwait(false);
            CacheStatus = new MicrosoftCalendarCacheStatus(MicrosoftCalendarDataState.Fresh, refreshedAt, null);
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            var cached = await _cache.TryLoadCalendarsAsync(accountId, cancellationToken).ConfigureAwait(false);
            if (cached is null)
            {
                CacheStatus = new MicrosoftCalendarCacheStatus(MicrosoftCalendarDataState.Empty, null, exception.Message);
                throw;
            }
            calendars = cached.Calendars;
            CacheStatus = new MicrosoftCalendarCacheStatus(
                MicrosoftCalendarDataState.OfflineCache, cached.RefreshedAt, exception.Message);
        }
        _knownCalendars.Clear();
        foreach (var calendar in calendars) _knownCalendars[calendar.Id] = calendar;
        return calendars;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        IReadOnlyList<CalendarSource> calendars, DateTimeOffset start, DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        var token = await _authentication.AcquireTokenSilentAsync(ReadScopes, cancellationToken)
            .ConfigureAwait(false);
        var query = new EventCacheQuery(RequireAccountId(), calendars.Select(calendar => calendar.Id).ToArray(), start, end);
        try
        {
            var events = await _graph.GetEventsAsync(token, calendars, start, end, cancellationToken)
                .ConfigureAwait(false);
            var refreshedAt = DateTimeOffset.UtcNow;
            await _cache.SaveAsync(query, events, refreshedAt, cancellationToken).ConfigureAwait(false);
            CacheStatus = new MicrosoftCalendarCacheStatus(MicrosoftCalendarDataState.Fresh, refreshedAt, null);
            return events;
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            var cached = await _cache.TryLoadAsync(query, cancellationToken).ConfigureAwait(false);
            if (cached is null)
            {
                CacheStatus = new MicrosoftCalendarCacheStatus(MicrosoftCalendarDataState.Empty, null, exception.Message);
                throw;
            }

            CacheStatus = new MicrosoftCalendarCacheStatus(
                MicrosoftCalendarDataState.OfflineCache, cached.RefreshedAt, exception.Message);
            return cached.Events;
        }
    }

    public async Task CreateEventAsync(EventDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!_knownCalendars.TryGetValue(draft.CalendarId, out var calendar))
        {
            await GetCalendarsAsync(cancellationToken).ConfigureAwait(false);
            if (!_knownCalendars.TryGetValue(draft.CalendarId, out calendar))
                throw new ArgumentException("The selected calendar is unavailable.", nameof(draft));
        }
        if (!calendar.CanEdit)
            throw new CalendarReadOnlyException($"Calendar '{calendar.Name}' is read-only.");

        string token;
        try
        {
            token = await _authentication.AcquireTokenSilentAsync(WriteScopes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MicrosoftCalendarAuthenticationRequiredException)
        {
            token = await _authentication.AcquireTokenInteractiveAsync(WriteScopes, cancellationToken)
                .ConfigureAwait(false);
        }
        await _graph.CreateEventAsync(token, calendar, draft, cancellationToken).ConfigureAwait(false);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _authentication.SignOutAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _knownCalendars.Clear();
            _cache.Clear();
            CacheStatus = new MicrosoftCalendarCacheStatus(MicrosoftCalendarDataState.Empty, null, null);
        }
    }

    public void Dispose() => _ownedHttpClient?.Dispose();

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        exception is HttpRequestException or TaskCanceledException;

    private string RequireAccountName() => _authentication.AccountName ??
        throw new MicrosoftCalendarAuthenticationRequiredException("Connect a Microsoft account first.");

    private string RequireAccountId() => _authentication.AccountId ??
        throw new MicrosoftCalendarAuthenticationRequiredException("Connect a Microsoft account first.");
}

internal sealed class MsalAccessTokenProvider(IPublicClientApplication application) : IAccessTokenProvider
{
    private IAccount? _account;

    public string? AccountName => _account?.Username;
    public string? AccountId => _account?.HomeAccountId?.Identifier;

    public async Task<string> AcquireTokenSilentAsync(IReadOnlyList<string> scopes,
        CancellationToken cancellationToken)
    {
        var account = _account ?? (await application.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault()
            ?? PublicClientApplication.OperatingSystemAccount;
        try
        {
            var result = await application.AcquireTokenSilent(scopes, account)
                .ExecuteAsync(cancellationToken).ConfigureAwait(false);
            _account = result.Account;
            return result.AccessToken;
        }
        catch (MsalUiRequiredException exception)
        {
            throw new MicrosoftCalendarAuthenticationRequiredException(
                "Microsoft needs an explicit sign-in or additional calendar consent.", exception);
        }
    }

    public async Task<string> AcquireTokenInteractiveAsync(IReadOnlyList<string> scopes,
        CancellationToken cancellationToken)
    {
        var result = await application.AcquireTokenInteractive(scopes)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(cancellationToken).ConfigureAwait(false);
        _account = result.Account;
        return result.AccessToken;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        foreach (var account in await application.GetAccountsAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await application.RemoveAsync(account).ConfigureAwait(false);
        }
        _account = null;
    }
}
