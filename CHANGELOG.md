# Changelog

All notable changes to Agenda are documented here. The current project version is **0.3.0**; future work should use an **Unreleased** section until the next version is prepared.

## [0.3.0] — 2026-09-11

### Added

- Native Windows meeting notifications with an off-by-default master switch, multiple reminder times, and per-occurrence overrides in event details. Right-click an agenda entry to access details and reminder controls. Visible timed events are checked while the app runs in the tray; delivery keys prevent duplicates across refreshes and restarts.
- Notification sound selection in Settings and per-event reminder overrides, with built-in Windows sounds and a Silent option that keeps visual notifications enabled.

### Changed

- Reminder times appear in a left-aligned dropdown beside the notification toggle, ordered from at-start to one hour before. Event details use a **Use custom reminders** toggle; dropdowns no longer resize the dialog.
- Removed explanatory text from Settings and event reminder controls for a more compact interface.
- The run script automatically restores dependencies and performs an incremental build before launch, stopping if the build fails.
- Expanded the README with user instructions and the main application screenshot.

### Fixed

- Self-contained builds now bundle the Windows App Runtime resource DLL required by notification registration, preventing error `0x8007007E`. Packaging checks that the DLL is included.

## [0.2.0] — 2026-09-11

### Added

- Clicking an agenda event opens its event-specific source link when available. Windows appointments, Microsoft Graph web links, and HTTPS ICS VEVENT URLs are supported; events without a deep link now open an in-app details view with optional source-calendar actions.
- The actual current month and its neighbors remain pinned in memory independently of the browsing buffer, including when browsing distant dates. Refreshes replace overlapping data without resurrecting deleted events.
- A rolling five-month event buffer keeps the displayed month and its neighbors ready for navigation, refills before adjacent-month coverage runs out, and reuses loaded events for up to five minutes between refreshes.
- Scroll the mouse wheel over the month calendar to navigate months, with precision-wheel accumulation and the same regional calendar behavior as the month buttons.
- Calendar feeds can be reordered by dragging an entry's name or background, with native drag feedback and scrolling, persistent order, and Alt+Up/Down keyboard support on focused entries.

### Changed

- The calendar count button now toggles calendar visibility selection open and closed; redundant Back/feed-settings buttons and the feed information section in Settings were removed.
- Reduced the agenda overflow indicator's height and bottom padding to leave more space for events.
- Moved the Settings version label to the bottom-right above the footer, outside the scrolling settings content.

### Fixed

- Adding, editing, and removing feeds keeps feed management open throughout the calendar refresh, without briefly switching to the agenda or navigating back after completion.
- Development certificate trust now targets Local Machine Trusted People and requires administrator privileges, correcting MSIX installation error 0x800B0109 after current-user signature verification succeeded.

## [0.1.0] — 2026-09-10

### Added

- Native WinUI 3 Windows 11 calendar flyout with acrylic styling, notification-area activation, `Ctrl+Alt+A`, monitor-aware positioning, and per-monitor DPI handling.
- Regional calendar behavior, including culture-specific calendars, first weekday, week rules, week numbers, and an ISO 8601 override.
- Selected-day navigation, collapsible month view, agenda overflow scrolling, and source-aware day markers.
- Read-only HTTPS and local iCalendar feed support for personal Google, Outlook.com, and other `.ics` calendars.
- Encrypted feed configuration and event caches protected for the current Windows user.
- Feed add/edit dialogs, per-feed colors, an 18-color Windows-themed palette, color swatches, edit controls, and remove controls.
- Windows appointment-store and optional Microsoft Graph provider implementations, retained behind the current settings surface.
- Automated core, provider, shell, native smoke, portable startup, x64 packaging, and ARM64 compilation checks.
- GitHub Actions verification workflow, issue template, repository attributes, publishing documentation, and architecture diagrams.
- A single `AgendaVersion` source, Settings version display, package-version synchronization, and a pre-tag release metadata check.
- Signed MSIX packaging with optional persistent local development certificates, optional publisher PFX input, and an explicit certificate-trust helper for Windows trust stores.

### Changed

- Settings and feed management are opened and closed from the footer controls; the former in-panel completion and experimental taskbar-clock controls are hidden.
- The calendar view uses thin, theme-aware native-style iconography and a focused scroll region so the flyout frame and footer remain stable while agenda entries scroll.
- Feed source edits preserve protected data only when the source identity still matches; changing a URL or file path requires a successful refresh before offline reuse.
- Package generation remains local and produces signed output under ignored `artifacts/`; package binaries and signing certificates are excluded from source control.

### Fixed

- Duplicate notification callbacks that could toggle the flyout twice for one tray click.
- Tray deactivation races that caused a flyout to flicker or reopen while the user was clicking its icon.
- Classic Win32 frame artifacts and diagonal tinting around the WinUI flyout.
- Calendar day sizing and event-marker layout so selected dates remain circular and markers stay visible below date labels.
- Regional refresh, month navigation, cache identity, and stale-feed status handling across provider changes.

### Validation

- 19 calendar-core checks, 33 provider checks, and 13 shell checks pass on Windows 11 x64 with .NET SDK 10.0.301.
- Release x64 build completes with zero warnings and errors.
- ARM64 Release configuration compiles successfully; ARM64 execution and signed installation remain environment-dependent checks.
