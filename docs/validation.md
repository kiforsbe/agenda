# Validation — 2026-09-10

## Verified on this machine

- Windows 11 x64, build 26200; .NET SDK 10.0.301 and Windows SDK 26100.
- Native WinUI app build: zero warnings and errors.
- `scripts/test.ps1`: 19 calendar core checks, 33 provider checks, 13 shell checks passed, exit 0. Provider checks use deterministic local fixtures and mocked transport/authentication, not personal accounts.
- Core coverage includes Swedish and US week rules, first-weekday overrides, ISO year rollover, Persian regional weeks, timezone overlap and exclusive all-day end dates.
- Provider coverage includes Graph paging and consent boundaries, read-only rejection, encrypted caches and sign-out, ICS recurrence exceptions/replacements, all-day overlap, private URL redaction, partial feed failures, HTTPS redirects, download/evaluation limits and offline fallback.
- Native shell smoke: window positioning/show/hide, tray lifecycle and Ctrl+Alt+A registration passed. Clock discovery reported no independently bounded Win32 clock target; Windows retains normal clock clicks.
- Actual WinUI executable launched and its Agenda window was enumerated. Windows appointment-store probe returned one calendar and zero events for the range from seven days before the probe through thirty days after it. Event titles and account details were not written to the probe log.
- `scripts/package.ps1 -Architecture x64` produced a self-contained portable directory and `artifacts/Agenda-x64.msix`; MakeAppx validation and packaging succeeded.
- Portable startup regression: first failed because publishing omitted `Agenda.App.pri` and compiled XAML. The publish target now includes those resources explicitly. `scripts/test-portable.ps1` then passed: the native process remained running without logged exceptions. This matches the upstream [unpackaged publish resource issue](https://github.com/microsoft/WindowsAppSDK/issues/6720).
- ARM64 Release configuration compiled with zero warnings and errors; it was not executed on ARM hardware.

## Review changes

Independent code review identified stale OS calendar enumeration, a midnight reopening issue, stale event ranges after regional changes, and suspend visibility tracking. The app now re-enumerates Windows calendars, rerenders when opened, refreshes changed ranges, and queries actual native visibility when toggled. Connection changes validate/save before replacing a working provider.

## Follow-up: tray and visual fixes

- Reproduced duplicate tray activation in the real callback: one version-4 click sequence caused two toggles. The callback now separates version-4 semantic notifications from legacy mouse notifications. The regression and ten existing shell checks pass.
- Day buttons now have equal 40-DIP dimensions. Each event marker has its own layout row and a 6-DIP diameter. Live native inspection verified circular today/selected states and clear markers below the date numbers.
- Removed the extra root tint layer. The user confirmed the diagonal artifact was gone.
- WinUI retained a classic dialog frame even while its presenter reported `HasBorder=false`. A scoped subclass of Agenda's own window now makes the entire rectangle client content through `WM_NCCALCSIZE`; DWM supplies rounded corners. The native regression first reported a 184-pixel client width inside a 200-pixel window, then passed with both dimensions equal. Live screenshots verified the white frame is gone and acrylic remains.
- Selected an empty day in the live app and verified the circular selection ring, agenda update and stable layout. Independent review checked the subclass lifetime and pointer-sized interop.
- Repeated physical tray clicks and the exact ordering of outside-click dismissal still need interactive confirmation; the automated callback test verifies duplicate-notification handling and the delayed-dismissal path.

## Remaining manual checks

- Initial screenshot inspection was blocked by an approval timeout; follow-up inspection succeeded as recorded above. Full keyboard and screen-reader coverage is still pending.
- User-configured feeds were visible during follow-up inspection. Provider parsing/download/cache behavior is tested with fixtures; end-to-end freshness and changes at each remote calendar service still need checking.
- Microsoft WAM/Graph live sign-in and event creation require an app registration and explicit user consent. No real calendar entries were created or changed.
- Multiple monitor hardware, HDR, high contrast, screen readers, taskbar auto-hide and Explorer restart need interactive validation. Geometry and strict clock hit testing have automated coverage.
- ARM64 runtime, signed MSIX installation and clean-machine installation have not been verified. The generated MSIX is unsigned and was not installed. No certificate or startup registration was added.

## Try the app

Run `scripts/build.ps1`, then `scripts/run.ps1`. Open Agenda from its notification-area icon or Ctrl+Alt+A. In Settings choose Manage iCalendar feeds; Add calendar feed opens the entry popup. Other provider setup controls are hidden, and taskbar-clock interception is disabled. Week numbers are enabled by default; ISO mode explicitly forces Monday and the ISO first-week rule.

After packaging, exit other Agenda instances and run `scripts/test-portable.ps1 -Directory <the portable directory printed by package.ps1>` to check resource inclusion and startup. This check launches and then closes its own isolated test instance.

For an isolated profile use `scripts/run.ps1 -DataDirectory ./work/manual-profile`. Exit any running Agenda instance first: one instance runs per Windows session. Right-click its notification icon and choose Exit.
