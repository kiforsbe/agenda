using System.Net;
using System.Runtime.CompilerServices;
using Agenda.Core;

[assembly: InternalsVisibleTo("Agenda.ProviderTests")]

namespace Agenda.Providers;

public sealed class GraphApiException(HttpStatusCode statusCode, string errorCode, string message)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}

public sealed class GraphProtocolException : Exception
{
    public GraphProtocolException(string message) : base(message) { }
    public GraphProtocolException(string message, Exception innerException) : base(message, innerException) { }
}
public sealed class CalendarReadOnlyException(string message) : InvalidOperationException(message);

internal sealed record EventCacheQuery(
    string AccountId,
    IReadOnlyList<string> CalendarIds,
    DateTimeOffset Start,
    DateTimeOffset End);

internal sealed record EventCacheSnapshot(
    DateTimeOffset RefreshedAt,
    IReadOnlyList<CalendarEvent> Events);

internal sealed record CalendarCacheSnapshot(
    string AccountId,
    DateTimeOffset RefreshedAt,
    IReadOnlyList<CalendarSource> Calendars);
