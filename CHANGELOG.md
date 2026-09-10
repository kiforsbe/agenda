# Changelog

All notable changes to Agenda are documented here. The project has not published a numbered release yet; the current work is tracked as **Unreleased**.

## [Unreleased] — 2026-09-10

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

### Changed

- Settings and feed management are opened and closed from the footer controls; the former in-panel completion and experimental taskbar-clock controls are hidden.
- The calendar view uses thin, theme-aware native-style iconography and a focused scroll region so the flyout frame and footer remain stable while agenda entries scroll.
- Feed source edits preserve protected data only when the source identity still matches; changing a URL or file path requires a successful refresh before offline reuse.
- Package generation remains local and produces unsigned output under ignored `artifacts/`; package binaries and signing certificates are excluded from source control.

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
