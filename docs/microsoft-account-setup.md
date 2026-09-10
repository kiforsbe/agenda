# Calendar account setup

Agenda supports three independent ways to read personal calendars:

1. Microsoft Graph with the native Windows account picker. This supports reading and creating events, but it requires your own Microsoft Entra public-client registration.
2. HTTPS or local iCalendar (`.ics`) feeds. These need no app registration and are read-only.
3. The Windows appointment store, selected by **Use Windows calendars**. This needs no client ID and is read-only. The app reads only calendars that Windows exposes to it; new Outlook's configured accounts may not synchronize to this store. Windows account management can be opened from Agenda settings, but does not grant Agenda access to Outlook's private data.

Agenda does not contain a borrowed Microsoft client ID, client secret, or example account. It tries the Windows store by default. If that is unavailable or empty, choose a feed; Microsoft registration is optional.

## Option 1: Microsoft Graph with a personal account

You do not need a company or a Microsoft 365 business subscription. You do need a Microsoft Entra directory in which you can register an app. A personal Azure account can use the default directory included with an Azure account. Microsoft describes the [Microsoft Entra ID Free product](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/microsoft-entra-id-free) and its signup requirements; Microsoft may require a phone number and payment card for identity verification even when you use free services.

Use an existing/default directory if Azure created one for your account. Microsoft currently restricts creating additional Workforce tenants from the Entra admin center to paid customers; see [Create a new tenant in Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/fundamentals/create-new-tenant). This does not mean Agenda needs a business Microsoft 365 subscription.

1. Open [Microsoft Entra admin center](https://entra.microsoft.com/) and select **Identity > Applications > App registrations > New registration**.
2. Enter a name such as `Agenda (local)`.
3. For **Supported account types**, select **Accounts in any organizational directory and personal Microsoft accounts**. That audience is what lets WAM show the Windows account picker for both work/school and personal Microsoft accounts.
4. Register the app and copy its **Application (client) ID**. This GUID is the value Agenda needs. Do not copy the object ID or directory ID.
5. Under **Authentication**, add the **Mobile and desktop applications** platform and add this custom redirect URI, replacing the placeholder with the client ID:

   ```text
   ms-appx-web://microsoft.aad.brokerplugin/{client-id}
   ```

6. Under **Authentication > Advanced settings**, enable **Allow public client flows**. Agenda is a desktop public client and must not have a client secret.
7. Under **API permissions**, add Microsoft Graph delegated permission **Calendars.Read**. Agenda requests it when you explicitly select Connect. Add **Calendars.ReadWrite** to the registration as well; Agenda requests its consent incrementally only when you explicitly save an event. Tenant policy can require an administrator to approve delegated permissions.
8. Put the Application (client) ID in Agenda settings, then choose **Connect Microsoft account**.

Microsoft documents the [WAM/MSAL.NET setup, system picker, parent-window requirement, and redirect URI](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam). The Graph operations use the documented delegated permissions for [listing calendars](https://learn.microsoft.com/en-us/graph/api/user-list-calendars?view=graph-rest-1.0), [expanded calendar views](https://learn.microsoft.com/en-us/graph/api/calendar-list-calendarview?view=graph-rest-1.0), and [creating events](https://learn.microsoft.com/en-us/graph/api/user-post-events?view=graph-rest-1.0).

Agenda opens interactive WAM UI only after Connect, or after Save when write consent is missing. Background refresh uses silent token acquisition and surfaces a sign-in-required error instead of opening an unexpected prompt. Sign-out removes the app's MSAL account entries and both protected Microsoft cache files. WAM manages its own supported token cache; Agenda never writes tokens to its storage directory.

## Option 2: calendar feeds without app registration

Calendar feeds are useful when you only need to see events. They cannot create, edit, or delete events.

### Google Calendar

In Google Calendar on the web, open **Settings > Settings for my calendars > your calendar > Integrate calendar**, then copy **Secret address in iCal format**. Google's [official subscription instructions](https://support.google.com/calendar/answer/37648?hl=en) state that the secret address is private: anyone who obtains it can read the feed. Do not share it. If it leaks, use Google's **Reset** action to invalidate it and configure Agenda with the new address.

The public iCal address works only for a public calendar and exposes events publicly. Google's [public-calendar instructions](https://support.google.com/calendar/answer/37083?hl=en) explain that distinction.

### Outlook.com

In Outlook on the web, open **Settings > Calendar > Shared calendars**, choose a calendar under **Publish a calendar**, choose the detail level, select **Publish**, and copy the ICS link. Microsoft's [Outlook.com sharing instructions](https://support.microsoft.com/en-us/outlook/share-your-calendar-in-outlook-com) explain that published ICS calendars are read-only. Microsoft also warns that a published calendar is viewable by anyone who has its link; unpublish it if the link is exposed.

### Local file

Agenda can also read an absolute path to a local `.ics` file. A local file is reread during refresh and does not require network access.

### Feed behavior and storage

- Network locations must use HTTPS. `http`, `ftp`, `webcal`, `file` URIs, and relative file paths are rejected.
- Each response/file is limited to 5 MiB. Network requests time out after 20 seconds. Redirects are followed at most five times, and every target must still use HTTPS.
- Recurrence rules, `EXDATE`, recurrence replacements, standard time zones, `VTIMEZONE`, and exclusive all-day end dates are evaluated by [Ical.Net 5](https://github.com/ical-org/ical.net). Events that began before the requested range but still overlap it are included; cancelled occurrences are omitted.
- Evaluation stops at 50,000 occurrences per feed (including historical occurrences), rejects recurrence candidate combinations above that limit, and applies Ical.Net's 1,000 unmatched-increment limit. Cancellation and a five-second evaluation budget are checked between occurrences. The library does not expose cancellation inside individual parser/evaluator steps, so those checks are cooperative rather than a hard execution timeout. Very old or unusually dense feeds may need a smaller exported history. Obsolete `EXRULE` exclusions are rejected; feeds should use `EXDATE`.
- Agenda stores configured feed locations and downloaded feed text with Windows DPAPI `CurrentUser` protection. Secret URLs and event text are not written in plaintext. Protected data can be decrypted only under the same Windows user profile.
- When refresh or parsing fails and a valid protected cached copy exists, events are returned with `CalendarFeedDataState.OfflineCache`, the successful refresh timestamp, and a sanitized error message. Private feed URLs and raw network errors are not exposed through status messages.
- Each feed refresh is independent. Successful and cached feeds remain available when another feed fails. A failed feed without usable cache has `Empty` status and an error message. If every requested feed fails without usable cache, the provider throws `CalendarFeedException` after attempting them all. Callers must inspect every requested feed's status to label a partial result correctly.

The provider API is:

```csharp
using Agenda.Providers;

var storageDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Agenda");

IReadOnlyList<FeedConfiguration> feeds =
    await FeedConfigurationStore.LoadAsync(storageDirectory);

using var provider = new IcsCalendarProvider(feeds, storageDirectory);

await FeedConfigurationStore.SaveAsync(storageDirectory,
[
    new FeedConfiguration(
        Id: "personal-google",
        Name: "Personal",
        Location: secretIcsUrl,
        Color: "#4285F4")
]);
```

`IcsCalendarProvider.AccountName` is `Calendar feeds` when at least one feed is configured. Its `CalendarSource.CanEdit` values are always `false`, and `CreateEventAsync` always throws `CalendarReadOnlyException`. Inspect `FeedStatuses[feedId]` after refresh to distinguish `Fresh`, `OfflineCache`, and `Empty`.

The Microsoft provider constructor is:

```csharp
using var provider = new MicrosoftCalendarProvider(
    parentWindow: windowHandle,
    clientId: applicationClientId,
    storageDirectory: storageDirectory);
```

Inspect `MicrosoftCalendarProvider.CacheStatus` after calendar or event refresh. `OfflineCache` includes the timestamp of the last confirmed Graph response and the current network error. Network failures and request timeouts fall back to matching protected calendar metadata or event cache data; caller cancellation propagates. Authentication, consent, Graph authorization, malformed response, and write errors propagate as errors. Silent token acquisition must still succeed before either cache can be selected, so an expired token or unavailable account can prevent offline access. Background reads never open a sign-in prompt.
