# Native Agenda Implementation Plan

> **For agentic workers:** Use subagent-driven development for independent shell/provider tasks and review the integrated application. Steps use checkboxes for progress.

**Goal:** Build and run the approved Windows agenda flyout with regional week numbering, taskbar integration, Windows calendars, personal Google/Outlook ICS feeds and optional Microsoft Graph.

**Architecture:** Agenda.Core contains regional calendar logic and provider contracts; Agenda.Providers implements Graph and protected storage; Agenda.App owns WinUI and Win32 shell integration. Keep UI operations on its DispatcherQueue and external work cancellable.

**Tech Stack:** .NET 10, WinUI 3 / Windows App SDK, MSAL/WAM, Graph REST, Windows DPAPI, native Win32 interop, executable unit tests.

**Spec:** ../specs/2026-09-09-native-agenda-design.md

## Global constraints
- Windows 11, x64 runnable build; ARM64 build configuration.
- Real native controls/acrylic. Week numbers default on and support regional and ISO modes.
- User overrides take precedence over culture defaults. Changing region must not change stored event instants.
- No fabricated connected accounts or events. App registration is required for live Graph consent.
- Do not modify Explorer, install certificates, publish, or enable startup implicitly.

## Task 1: Calendar core and native UI (controller)
Files: src/Agenda.Core/{CalendarMath,Models}.cs, src/Agenda.App/{App,MainWindow,SettingsWindow}.xaml*, tests/Agenda.Tests/Program.cs.
- [x] Create projects, contracts, and failing tests for ISO Jan 1 2021 = 2020/W53, Jan 4 2021 = W1, Sunday first grid, culture overrides and event overlap.
- [x] Implement date-only month rows and regional week labels using CalendarWeekRule; verify tests.
- [x] Create the WinUI flyout with accessible day buttons, week column, scrollable agenda, calendar selection, quick editor, settings and native materials.
- [x] Wire real provider states, regional updates, and shell lifetime; compile and run.

## Task 2: Microsoft account/calendar provider (independent worker)
Files: src/Agenda.Providers/**, tests/Agenda.ProviderTests/**, docs/microsoft-account-setup.md.
Consumes: Agenda.Core CalendarSource, CalendarEvent, EventDraft, ICalendarProvider (see Models.cs).
Produces: MicrosoftCalendarProvider(nint parentWindow, string clientId, string storageDirectory), provider methods and protected cache.
- [x] Write failing transport-boundary tests for paging, time zones, all-day events, error propagation and read-only writes.
- [x] Implement MSAL public client/WAM, Graph calendar enumeration, calendarView range query, event creation and sign-out; use delegated read permissions and incremental write consent.
- [x] Implement user-protected cache and correct invalidation on sign-out; verify tests.
- [x] Document public client registration, redirect URI and permissions with official references.

## Task 3: Native shell host (independent worker)
Files: src/Agenda.App/Shell/**, docs/taskbar-integration.md.
Produces: ShellHost(nint window, Action toggle, Action exit, Action settings, Action regionalChanged), SetClockIntegration(bool), PositionFlyout(int widthDip,int heightDip), Show(), Hide(), IDisposable. Callbacks marshaled by UI.
- [x] Implement notification icon and message-window lifecycle, Explorer restart handling, positioning/DPI/work-area clamping and activation.
- [x] Implement opt-in clock click interception with strict bounds validation and fail-open behavior. No Explorer injection or broad input capture persistence.
- [x] Expose support status and document compatibility boundary; compile integration with App.

## Task 4: Integration, distribution and review
Files: README.md, scripts/{build,run,package}.ps1, deployment/**, docs/validation.md.
- [x] Build x64, execute core/provider tests and validate failure paths without credentials.
- [ ] Launch and inspect actual native UI; test week column, settings, tray activation and dismissal; record limitations precisely.
- [x] Add packaging scripts and registration docs; verify scripts are usable.
- [x] Independent review of whole change; resolve actionable findings and repeat affected checks.

## Progress
Initial repository is empty, no baseline tests exist. Build SDK 10.0.301 and Windows SDK 26100 are installed. Work is in the user-selected repository on a feature branch; no linked worktree is needed for an empty repository.

## Scope update and verification
The user requested alternatives to Entra registration. Implemented Windows appointment-store reading and encrypted personal Google/Outlook ICS feeds; both are read-only, with provider web links for editing. Graph remains optional. Native x64 startup, 57 automated checks, shell lifecycle smoke, x64 portable packaging, and ARM64 compilation passed. Interactive visual QA remains unchecked because computer-use app approval timed out. See docs/validation.md for exact boundaries and the Windows-store/clock findings.
