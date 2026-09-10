# Agenda

A native Windows 11 calendar flyout built with C#, WinUI 3 and Windows App SDK.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the component boundaries, runtime flows, protected-data model, and Mermaid diagrams.
See [CHANGELOG.md](CHANGELOG.md) for the current unreleased feature and fix history.

## Build and run

Requirements: Windows 11, .NET 10 SDK and Windows SDK 10.0.26100.0. The Windows App SDK is restored from NuGet; no browser rendering engine is used for the flyout.

```powershell
./scripts/build.ps1
./scripts/run.ps1
./scripts/test.ps1
```

The app opens above the taskbar and remains in the notification area when dismissed. If Windows places its icon in the overflow menu, open that menu to find Agenda. Right-click its icon for Open, Settings and Exit. Only one instance runs per Windows session.

For an isolated development profile:

```powershell
./scripts/run.ps1 -DataDirectory ./work/profile
```

## Calendar behaviour

- Real Windows controls and desktop acrylic; system theme, accent and transparency fallbacks.
- Month navigation, keyboard day selection, previous/next-day buttons around Today, a collapsible month and selected-day agenda. Month markers show each visible feed's color: one dot for one source, or a segmented pill for multiple sources.
- Week numbers on by default; Windows first weekday and week rule, or explicit ISO 8601 override. ISO forces Monday. The week-year can differ from the date's calendar year near New Year.
- Windows regional date/time formats and user overrides; reload on system settings changes. Regional week numbering follows the active calendar; ISO numbering uses Gregorian week-years, including when viewing a non-Gregorian month.
- Calendar visibility selection, event details and meeting links for the active source. iCalendar feeds are read-only; create or edit events in the calendar service that owns the feed.

## Connect calendars without an Entra registration

Use the calendar icon beside the bottom Settings button to open or close feed management, then choose **Add calendar feed**. A popup collects the calendar name and secret ICS link or absolute local `.ics` file path. Add saves the feed; Cancel dismisses the draft. Personal Google Calendar secret iCal links and Outlook.com published ICS links work without an Azure/Entra or Google developer registration. Multiple feeds can be displayed together.

Feeds are **read-only**; create and edit events in your calendar service. Google secret links and Outlook published links grant read access to anyone who has the link. Agenda masks and encrypts saved links using Windows user protection. It does not publish your calendar automatically.

Each feed has a color swatch on the left, followed by pencil and trash controls. Click the swatch to change its color, or the pencil to edit its name, source, and color together. New feeds default to a least-used color from an 18-color palette. Cancel leaves the saved feed unchanged. Color-only edits from the swatch update the agenda without a network refresh.

Offline caches are tied to their source link or file path, so editing a source cannot reuse another calendar's events. Caches from builds before source tracking need one successful refresh before offline reuse.

Windows appointment-store and Microsoft sign-in management are currently hidden from Settings. Their implementation and previously saved source preferences remain in the app.

Feed refresh runs every five minutes while the app runs, with manual Refresh available. Service-side publication can delay updates. Recurring events, time zones and all-day events are handled; failed feeds use encrypted cached data where available and are marked as stale. See [account and feed setup](docs/microsoft-account-setup.md).

Only one source mode is active at a time: Windows, the collection of saved feeds, or Microsoft Graph. Saved feeds remain configured when switching modes or disconnecting; remove them in the feed screen to forget their links.

## Optional Microsoft sign-in and event creation

This integration is retained in code, but its sign-in and developer setup controls are currently hidden. When enabled in a future build, the developer must register the app once. End users of a properly configured release do not need Azure accounts or app registrations. The application's client ID is public configuration, not a password. No client secret is used.

See [Microsoft setup](docs/microsoft-account-setup.md) for personal-developer registration and exact authentication settings. There is no business or Microsoft 365 Business subscription requirement just to use a personal Outlook.com calendar. Azure registration may require identity/card verification.

Windows account sign-in is brokered through WAM; Graph supplies calendar data. A user must consent to this application's calendar access. Only one selected Microsoft identity is active at a time. Google account OAuth is not implemented; personal Google calendars use the feed connection above.

No real calendar data is fabricated when the app is unconfigured. The month calendar and settings work before authentication.

## Taskbar clock

The notification-area icon and **Ctrl+Alt+A** open the flyout. Taskbar-clock interception is disabled in code and its Settings control is removed, including for older profiles that enabled it. Windows handles clicks on its clock. This is an independent native window, not an injected extension to the Windows notification centre. See [compatibility details](docs/taskbar-integration.md) for the retained experimental implementation.

## Package

```powershell
./scripts/package.ps1 -Architecture x64
./scripts/package.ps1 -Architecture ARM64
```

The script creates a self-contained portable directory and an **unsigned** MSIX in `artifacts/`. The MSIX needs publisher-matching signing before installation/distribution. The script does not install certificates, register startup, install the package or publish to the Store. Use `-Publisher` to set the intended certificate's subject; Store publishing requires the identity assigned by Partner Center.

User settings/cache are under `%LOCALAPPDATA%/Agenda` unless `--data-dir` is supplied. Credentials belong to the Windows authentication broker; event caches are protected for the current Windows user. Disconnect clears the active provider's cache; configured feed links remain until removed in the feed screen.

See [validation](docs/validation.md) for what was actually tested and the remaining real-account checks. This repository does not yet include a software license; choose and add one before accepting third-party contributions or distributing the code.
