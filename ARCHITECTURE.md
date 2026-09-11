# Agenda architecture

Agenda is a native, unpackaged Windows 11 desktop application. It presents a compact calendar flyout, is activated from the notification area or a global shortcut, and reads calendar data through provider implementations. The design keeps WinUI presentation, Win32 shell integration, calendar rules, and remote data access in separate layers so that each can be tested without requiring a live account.

The editable application version lives in `Directory.Build.props` as `AgendaVersion`. MSBuild emits it as assembly informational, file, and assembly version metadata; `App.Version` reads that compiled value for the Settings page. The package script converts the same three-part value to the four-part MSIX manifest format, and `scripts/check-release.ps1` verifies that a `vX.Y.Z` tag and changelog heading match before release tagging.

## System boundary

The process owns the flyout window and its notification-area icon. Windows owns the taskbar, Explorer, the Windows account broker, and the appointment store. Agenda does not inject UI into Explorer, subclass the taskbar, or capture broad mouse input. Taskbar-clock interception is intentionally disabled; the supported entry points are the Agenda tray icon and `Ctrl+Alt+A`.

```mermaid
flowchart TB
    User((User))
    subgraph Windows[Windows 11 platform]
        Taskbar[Taskbar and notification area]
        Explorer[Explorer shell messages]
        WAM[Windows Account Manager]
        Store[Windows appointment store]
        DWM[DWM acrylic and window frame]
    end

    subgraph Agenda[Agenda process]
        Shell[Agenda.App\nShellHost + NativeMethods]
        Window[Agenda.App\nMainWindow + WinUI XAML]
        Core[Agenda.Core\nCalendarMath + models]
        Providers[Agenda.Providers\nprovider contracts and implementations]
        Storage[Protected local storage\nDPAPI settings and caches]
    end

    subgraph Services[Calendar services]
        Graph[Microsoft Graph]
        ICS[HTTPS or local iCalendar feeds]
    end

    User --> Taskbar
    Taskbar -->|tray activation| Shell
    Explorer -->|TaskbarCreated, settings, suspend/resume| Shell
    Shell -->|DispatcherQueue| Window
    Window --> Core
    Window --> Providers
    Providers --> Storage
    Providers --> Graph
    Providers --> ICS
    Window --> Store
    Providers --> WAM
    Window --> DWM
```

The diagram shows ownership rather than deployment. The Windows appointment store and WAM are optional runtime dependencies: the current Settings UI exposes iCalendar feed management, while the Windows-store and Graph implementations remain available in code for future or previously configured source modes.

## Code organization

### `Agenda.Core`

This is the platform-independent domain layer. It contains the shared calendar models and date-only rules used by every provider and by the calendar grid:

- `Models.cs` defines calendar sources, events, drafts, feed configuration, provider state, and regional settings contracts.
- `CalendarMath.cs` builds month grids, applies the selected culture/calendar, calculates week numbers, handles ISO overrides, and determines which feeds contribute a marker to a day.
- `FeedColorPalette.cs` owns the Windows-themed default palette and least-used color selection.

The core layer does not know about WinUI, HWNDs, HTTP, Graph, DPAPI, or the Windows account broker. Date-only calculations are kept separate from event instants so that changing regional settings does not move an event in time.

### `Agenda.Providers`

Providers convert external calendar systems into the shared core models. They are selected by the application connection coordinator and expose the common provider contracts.

- `IcsCalendarProvider` reads HTTPS or local `.ics` sources. It evaluates recurrence, time zones, exclusions, all-day ranges, redirects, limits, and partial-feed failures. ICS sources are read-only.
- `MicrosoftCalendarProvider` uses MSAL/WAM and Microsoft Graph when a valid public client ID and an account are configured. It supports the Graph calendar and event operations retained for optional future UI exposure.
- `WindowsCalendarProvider` is in `Agenda.App/Services` because it calls the Windows appointment-store API directly. It exposes the calendars that Windows makes available to the app and is read-only.
- `ProtectedCalendarCache` and the feed configuration store persist protected metadata and event text. Feed locations are kept inside the protected cache envelope so an edited source cannot accidentally reuse another source's events.
- `GraphCalendarClient` contains the HTTP-level Graph calls, paging validation, payload mapping, and error translation.

Provider failures are represented as status and error information where possible. A feed that fails can use a matching encrypted cache and is labelled stale; one failed feed does not hide successful feeds. Authentication and consent are never silently prompted during background refresh.

### `Agenda.App`

The application layer owns the WinUI window, presentation state, settings, provider selection, and shell lifetime.

- `App.xaml` and `App.xaml.cs` create the WinUI application and establish the per-user data directory and single-instance behavior.
- `MainWindow.xaml` defines the fixed flyout layout: clock, month grid, week-number column, selected-day heading, scrollable agenda region, settings, and feed dialogs.
- `MainWindow.xaml.cs` coordinates rendering, selection, regional changes, refresh, and panel navigation. Long operations use cancellation and an operation gate so an older refresh cannot overwrite newer state.
- `MainWindow.Connections.cs` loads configured feeds, switches providers, refreshes data, and opens the add/edit feed dialog. Microsoft and Windows-account controls are currently hidden while their implementation remains in the codebase.
- `MainWindow.FeedColors.cs` handles feed color edits, source-marker rendering, and the palette UI without forcing a network refresh for a color-only change.
- `Services/RegionalSettings.cs` reads Windows culture, first weekday, week rule, time format, and calendar settings, and raises changes when Windows settings change.
- `Shell/` contains the native notification icon, global hotkey, monitor/DPI geometry, DWM frame handling, and shell message window.

The UI is the only layer that directly schedules work on the WinUI dispatcher. Shell callbacks and provider continuations marshal back to that dispatcher before touching controls.

## Main runtime flows

### Activation and dismissal

```mermaid
sequenceDiagram
    actor User
    participant Tray as Windows tray
    participant Shell as ShellHost
    participant Queue as DispatcherQueue
    participant UI as MainWindow

    User->>Tray: Click Agenda icon
    Tray->>Shell: Shell notification callback
    Shell->>Queue: Enqueue ToggleFlyout()
    Queue->>UI: Show or hide flyout
    UI->>Shell: PositionFlyout() and Show()/Hide()
    Shell-->>UI: Deactivation or Escape notification
    UI->>UI: Hide without terminating process
```

`ShellHost` handles both modern notification version 4 callbacks and legacy mouse notifications, but does not process both protocols for one activation. During a tray gesture it briefly defers deactivation dismissal, which prevents the normal outside-click path from immediately reopening or closing the flyout. A second click on the tray icon toggles the existing window closed.

The shell host also listens for `TaskbarCreated`, display/settings changes, and suspend/resume. It recreates the icon after Explorer restarts, chooses the light or dark thin-line icon from the system theme, and clamps the flyout to the active monitor work area using effective DPI.

### Calendar refresh and rendering

```mermaid
sequenceDiagram
    participant UI as MainWindow
    participant Provider as ICalendarProvider
    participant Source as Windows store, Graph, or ICS
    participant Cache as Protected cache
    participant Core as CalendarMath

    UI->>Provider: Get calendars and events(range, cancellation)
    Provider->>Source: Read source data
    alt source succeeds
        Source-->>Provider: Calendar data
        Provider->>Cache: Save protected response and source identity
    else source fails
        Provider->>Cache: Load matching protected cache
        Cache-->>Provider: Stale data or no data
    end
    Provider-->>UI: Events, sources, statuses, and errors
    UI->>Core: Build month and day markers
    Core-->>UI: Regional grid, week labels, and source groups
    UI->>UI: Render fixed calendar and scrollable agenda
```

The month grid is fixed while only the selected-day agenda scrolls. A day marker groups events by visible feed source: one source is a dot and multiple sources are represented by adjacent color segments. Hidden calendars are excluded before markers are calculated.

### Feed editing

Feed management is deliberately read-only with respect to the remote calendar. The add and edit dialog validates the name, source location, and palette color, then encrypts the saved configuration for the current Windows user. Editing a name or color preserves the feed identity; editing the source location invalidates reuse of the old source's cache until the new source refreshes successfully.

## Class relationships

```mermaid
classDiagram
    class MainWindow {
        +RefreshAsync()
        +ToggleFlyout()
        +SetPanel(name)
        -ICalendarProvider _provider
        -CancellationTokenSource _refreshCancellation
    }
    class ShellHost {
        +Show()
        +Hide()
        +PositionFlyout(width, height)
        +Dispose()
        -NotifyIcon()
        -HotkeyWindow()
    }
    class CalendarMath {
        <<static>>
        +BuildMonthGrid(...)
        +WeekNumber(...)
        +SourcesOnDay(...)
    }
    class ICalendarProvider {
        <<interface>>
        +GetCalendarsAsync()
        +GetEventsAsync(range)
        +CreateEventAsync(draft)
    }
    class IcsCalendarProvider {
        +GetCalendarsAsync()
        +GetEventsAsync(range)
        +CreateEventAsync(draft)
    }
    class MicrosoftCalendarProvider {
        +GetCalendarsAsync()
        +GetEventsAsync(range)
        +CreateEventAsync(draft)
    }
    class WindowsCalendarProvider {
        +GetCalendarsAsync()
        +GetEventsAsync(range)
    }
    class FeedConfigurationStore {
        <<protected storage>>
        +LoadAsync(directory)
        +SaveAsync(directory, feeds)
    }
    class RegionalSettings {
        +Current
        +Changed
    }

    MainWindow --> ShellHost : activation and geometry
    MainWindow --> CalendarMath : grid and markers
    MainWindow --> ICalendarProvider : active source
    MainWindow --> RegionalSettings : presentation settings
    IcsCalendarProvider ..|> ICalendarProvider
    MicrosoftCalendarProvider ..|> ICalendarProvider
    WindowsCalendarProvider ..|> ICalendarProvider
    IcsCalendarProvider --> FeedConfigurationStore : encrypted feed config/cache
    MicrosoftCalendarProvider --> FeedConfigurationStore : encrypted Graph cache
```

`CreateEventAsync` is part of the shared contract for the optional Graph provider. The ICS provider rejects writes as read-only, and the Windows appointment-store provider currently exposes read-only data. The UI hides non-ICS management controls until those workflows are intentionally re-enabled.

## State and concurrency

The application has three independent kinds of state:

1. **Presentation state** — selected date, month, collapsed/expanded state, active panel, visible calendars, regional settings, and scroll position. This is held by `MainWindow` and rebuilt when system settings change.
2. **Connection state** — the active provider, configured feed list, refresh timestamps, per-feed status, and cancellation handles. Provider replacement occurs only after new configuration validates and saves successfully.
3. **Protected persistent state** — settings, feed locations, and event caches under `%LOCALAPPDATA%\\Agenda` (or the explicit development data directory). DPAPI `CurrentUser` protection binds this data to the Windows user profile.

Refreshes are cancellable and serialized by an operation gate. A refresh captures the requested date range and regional time zone, so a result for an older range or provider cannot replace a newer selection. Dialog saves use a deferral and validation before committing changes; cancellation clears the draft.

## Security and privacy boundaries

- Secret ICS URLs and cached event text are encrypted with Windows user protection and are never intended for source control.
- Network feeds require HTTPS, enforce response and recurrence limits, and follow only HTTPS redirects.
- Status messages sanitize private URLs and raw transport errors.
- Graph uses a public client ID and delegated consent through WAM; no client secret is embedded or required.
- Feed connections are read-only. Anyone who obtains a secret Google or published Outlook ICS link can read that feed, so users must treat links as credentials and rotate them at the provider if exposed.
- The repository ignores local profiles, build output, package files, and signing certificates. MSIX output is a local packaging artifact and is not published from this repository.

## Packaging and deployment

The normal development build is an unpackaged WinUI 3 executable. `scripts/build.ps1` restores and builds x64 or ARM64. `scripts/package.ps1` creates a self-contained portable directory and a signed MSIX in the ignored `artifacts/` directory. By default it uses a temporary development certificate; `-UseCertificateStore` instead creates or reuses a self-signed certificate in `Cert:\CurrentUser\My`, signs with its private key, exports only the public `.cer`, and leaves trust installation to the explicit `scripts/trust-dev-certificate.ps1` command, which requires administrator privileges and imports the public signing certificate into Local Machine Trusted People. Current-user stores do not satisfy MSIX deployment trust; importing it as a root CA is unnecessary. A publisher-matching PFX can be supplied for distribution signing. The scripts do not register startup, install the package automatically, or publish to the Store. `deployment/Assets` contains source artwork and tray icons used by both unpackaged and packaged output.

## Test seams

The repository uses small executable test projects rather than a test framework:

- `tests/Agenda.Tests` exercises regional date math, week-year boundaries, event overlap, and feed-color grouping.
- `tests/Agenda.ProviderTests` uses deterministic fixtures and mocked transport/authentication for Graph, ICS parsing, recurrence, limits, protected caches, and failure behavior.
- `tests/Agenda.ShellTests` links the shell sources directly and checks geometry, strict clock-target validation, tray lifecycle, notification callback de-duplication, and native smoke behavior.

The GitHub Actions workflow runs the full test script and a Release x64 build on Windows. Live account consent, remote feed freshness, multiple-monitor variations, signed installation, and ARM64 execution remain environment-dependent manual checks.

## Extension points

New calendar sources should implement `ICalendarProvider` and map into the existing `CalendarSource`, `CalendarEvent`, and provider-status models. They should keep authentication and transport details inside the provider, use cancellation, expose truthful read/write capabilities, and protect any cache with a source identity. UI changes should consume those contracts through `MainWindow.Connections.cs` rather than calling remote APIs from XAML event handlers.
