# Taskbar integration

Agenda uses supported desktop APIs and keeps its taskbar integration outside Explorer. The notification-area icon is the primary entry point. Its left click opens or hides the flyout, and its context menu provides **Open Agenda**, **Settings**, and **Exit**. The shell host also registers **Ctrl+Alt+A** as a global fallback to open or hide the flyout; `HotkeyStatus` reports when another application already owns that shortcut.

The icon is recreated after the registered `TaskbarCreated` message, which covers an Explorer restart. The shell host listens for suspend/resume notifications, hides the flyout during suspend, and refreshes the icon and optional clock discovery after resume. It first uses the executable's associated icon and falls back to the stock application icon when the executable has no extractable icon.

## Application integration

Construct `ShellHost` after the WinUI window has a real HWND:

```csharp
var shell = new ShellHost(
    windowHandle,
    toggle: () => dispatcherQueue.TryEnqueue(ToggleFlyout),
    exit: () => dispatcherQueue.TryEnqueue(ExitApplication),
    settings: () => dispatcherQueue.TryEnqueue(OpenSettings),
    regionalChanged: () => dispatcherQueue.TryEnqueue(RefreshRegionalPresentation));
```

Notification, hotkey, system-setting, and clock callbacks run on the shell message thread. The application must marshal UI work to its `DispatcherQueue`, as shown above. Before showing the window, call `PositionFlyout(widthDip, heightDip)` and then `Show()`. Call `Hide()` for deactivation, Escape, or outside-click dismissal. Dispose the host during application shutdown so the tray icon, hotkey, notification registration, and any mouse hook are removed.

`PositionFlyout` chooses the monitor containing the taskbar activation point when available, otherwise the monitor nearest the flyout. It reads that monitor's work area and effective DPI, converts the requested DIP size, anchors next to top, bottom, left, or right taskbars, and clamps the result to the work area.

## Optional taskbar-clock compatibility

Clock redirection is off by default. `SetClockIntegration(true)` opts in, and `ClockIntegrationStatus` (also exposed as `ClockSupportStatus`) gives the current result.

The compatibility layer accepts only a visible `TrayClockWClass` child whose root is exactly `Shell_TrayWnd` or `Shell_SecondaryTrayWnd`. It reads the real HWND rectangle, requires that rectangle to stay inside the taskbar, applies DPI-scaled size and area limits, and confirms the hit-tested HWND is that clock or one of its children before consuming a left click. Injected input, right clicks, notification-area hits, guessed screen-edge regions, unknown UI trees, and stale HWNDs pass through to Windows. A captured press that is dragged away is consumed without opening Agenda, preventing an unmatched button-up from reaching Explorer.

Modern Windows 11 taskbars often render the clock in an internal XAML tree without an independently bounded `TrayClockWClass`. On those builds the status reads **Unavailable**, no global mouse hook remains installed, and Windows continues to handle the clock normally. The 2026-09-09 native probe on Windows build 26200 observed this fail-open result. Agenda does not use approximate clock rectangles because that could capture the notification area or alter Win+N behavior.

Explorer injection, taskbar subclassing, private Explorer interfaces, registry patches, and broad persistent input capture are intentionally absent. A UI Automation fallback is also absent until Microsoft exposes a stable element identity and boundary contract suitable for strict validation. The notification icon and Ctrl+Alt+A remain available on unsupported taskbar layouts.

## Validation

The persistent shell harness links the production shell sources directly and needs no test framework package:

```powershell
dotnet run --project tests/Agenda.ShellTests/Agenda.ShellTests.csproj
dotnet run --project tests/Agenda.ShellTests/Agenda.ShellTests.csproj -- --smoke
```

The first command checks monitor-edge placement, work-area clamping, DPI selection, exact clock bounds, pass-through behavior, right-click preservation, and captured-drag handling. The smoke probe creates a temporary hidden target window and exercises shell startup, notification-icon lifecycle, executable-icon extraction, placement, show/hide, clock discovery, hotkey registration, and disposal. It reports environmental support statuses rather than failing when the clock target or global shortcut is unavailable.

The callback also checks the negotiated notification version: version 4 uses `NIN_SELECT`/`NIN_KEYSELECT` and `WM_CONTEXTMENU`; legacy callbacks use button-up notifications. Accepting both protocols at once caused the flyout to toggle twice for one click. The regression sends that sequence through the actual callback and requires one activation.

`FlyoutFrame.Attach` runs on the flyout's UI thread and subclasses only its own HWND. It handles `WM_NCCALCSIZE` to remove the unwanted classic dialog frame retained by fixed-size WinUI presenters, and forwards other messages. Keep its returned registration alive until window destruction or shutdown. The native smoke test verifies that client and outer dimensions match.

Relevant platform contracts: [notification icons](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw), [global hotkeys](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-registerhotkey), [low-level mouse hooks](https://learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc), and [per-monitor DPI](https://learn.microsoft.com/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor).
