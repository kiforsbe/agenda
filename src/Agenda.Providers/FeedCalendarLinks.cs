namespace Agenda.Providers;

public static class FeedCalendarLinks
{
    public static bool IsValidWebsite(string? value) =>
        Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme==Uri.UriSchemeHttps &&
        !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

    public static Uri? Resolve(FeedConfiguration feed)
    {
        if(IsValidWebsite(feed.CalendarWebsite))return new Uri(feed.CalendarWebsite!);
        if(!Uri.TryCreate(feed.Location,UriKind.Absolute,out var source) || source.Scheme!=Uri.UriSchemeHttps)return null;
        // Only inspect the host. Never launch the subscription URL or copy its
        // private token into a browser URL. ICS VEVENT.URL is not a calendar link.
        return source.IdnHost.ToLowerInvariant() switch {
            "calendar.google.com" => new Uri("https://calendar.google.com/calendar/u/0/r"),
            "www.google.com" when source.AbsolutePath.StartsWith("/calendar/",StringComparison.OrdinalIgnoreCase) => new Uri("https://calendar.google.com/calendar/u/0/r"),
            "outlook.live.com" or "outlook.com" or "www.outlook.com" => new Uri("https://outlook.live.com/calendar/0/view/month"),
            "outlook.office.com" or "outlook.office365.com" => new Uri("https://outlook.office.com/calendar/"),
            _ => null
        };
    }
}
