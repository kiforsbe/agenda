# Agenda

A native Windows 11 calendar flyout built with C#, WinUI 3 and Windows App SDK.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the component boundaries, runtime flows, protected-data model, and Mermaid diagrams.
See [CHANGELOG.md](CHANGELOG.md) for the current unreleased feature and fix history.

## Versioning and releases

The editable version is `AgendaVersion` in [Directory.Build.props](Directory.Build.props). It is compiled into the app and displayed in Settings; packaging derives the four-part MSIX version from the same value. Before creating a tag, update `AgendaVersion`, add or update the matching `## [major.minor.patch]` entry in [CHANGELOG.md](CHANGELOG.md), and run:

```powershell
./scripts/check-release.ps1 -Tag v0.1.0
git tag v0.1.0
```

The check fails when the tag, project version, or changelog heading does not match. Replace `0.1.0` with the new version in both the command and the files.

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

- Month navigation keeps a rolling five-month event buffer (two months before and after the displayed month) plus a separate buffer pinned around the actual current month. Browsing distant dates never evicts today's data. Nearby months render from memory; the browsing buffer refills when it no longer covers the displayed month and both neighbors. Manual and five-minute refreshes update both buffers; existing provider disk caches remain encrypted.
- Real Windows controls and desktop acrylic; system theme, accent and transparency fallbacks.
- Month navigation, keyboard day selection, previous/next-day buttons around Today, a collapsible month and selected-day agenda. Month markers show each visible feed's color: one dot for one source, or a segmented pill for multiple sources.
- Week numbers on by default; Windows first weekday and week rule, or explicit ISO 8601 override. ISO forces Monday. The week-year can differ from the date's calendar year near New Year.
- Windows regional date/time formats and user overrides; reload on system settings changes. Regional week numbering follows the active calendar; ISO numbering uses Gregorian week-years, including when viewing a non-Gregorian month.
- Calendar visibility selection, event details and meeting links for the active source. iCalendar feeds are read-only; create or edit events in the calendar service that owns the feed.

## Connect calendars without an Entra registration

Click an agenda event to open its event-specific source link when the provider supplies one. Windows appointments and Microsoft Graph events use their native event details; an ICS `VEVENT.URL` is used when it is an HTTPS event link. If no deep link exists, Agenda shows an in-app details view with the event time, source calendar, account, location, and available actions. For a source-calendar fallback, set **Calendar website (optional)** in Add/Edit feed. Leave it blank for automatic Google/Outlook detection. Private ICS subscription URLs are never opened as destinations.

Reorder feeds by dragging an entry's name or background; the list scrolls near its edges and saves the order on drop. Color, edit, and delete buttons retain their normal actions. Keyboard users can focus an entry and press **Alt+Up/Down**. Feed order is retained when reopening the app.

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

The script creates a self-contained portable directory and a **signed** MSIX in `artifacts/`. By default it uses a temporary self-signed development certificate and removes its private key afterward. To reuse the persistent certificate in the current user's Personal certificate store (`Cert:\CurrentUser\My`), pass `-UseCertificateStore`; it creates the certificate if needed and exports only its public certificate to `artifacts/signing/`. Trust that certificate once per computer from an administrator PowerShell before installing. MSIX deployment requires Local Machine Trusted People; current-user trust and a successful SignTool verification alone are insufficient:

```powershell
./scripts/package.ps1 -Architecture x64 -UseCertificateStore
# Run this trust step from PowerShell opened as Administrator:
./scripts/trust-dev-certificate.ps1  # imports the public cert into Local Machine Trusted People
# Install as the Windows user who will run Agenda:
Add-AppxPackage ./artifacts/Agenda-x64.msix
```

To create or inspect the persistent development certificate without packaging, run `./scripts/setup-dev-certificate.ps1`. For distribution, pass a publisher-matching PFX with `-CertificatePath` and, when needed, `-CertificatePassword`.

Store or certificate publishing remains a separate step; the scripts do not register startup, install the package automatically, or publish to the Store. Use `-Publisher` to set the intended certificate subject; Store publishing requires the identity assigned by Partner Center.

### Public distribution

A self-signed certificate is suitable for development, testing, or managed devices where an administrator deploys the certificate. It cannot make a direct GitHub download installable for every Windows user because each target device must explicitly trust that certificate.

For a public download, choose one of these paths:

- Submit the MSIX to the [Microsoft Store](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options). The Store re-signs accepted packages with a trusted certificate, so users do not install your development certificate.
- Use a CA-trusted code-signing certificate or [Azure Artifact Signing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) for direct distribution. Keep the PFX and password in a release system secret, run `package.ps1 -CertificatePath ...` in a release workflow, and publish only the signed MSIX.
- For an organization-managed rollout, deploy the self-signed or enterprise certificate through Group Policy, MDM, or another trusted device-management process before installing the package.

The repository's self-signed certificate scripts do not turn a certificate into a public trust anchor and must not commit a private key.

User settings/cache are under `%LOCALAPPDATA%/Agenda` unless `--data-dir` is supplied. Credentials belong to the Windows authentication broker; event caches are protected for the current Windows user. Disconnect clears the active provider's cache; configured feed links remain until removed in the feed screen.

See [validation](docs/validation.md) for what was actually tested and the remaining real-account checks. This repository does not yet include a software license; choose and add one before accepting third-party contributions or distributing the code.
