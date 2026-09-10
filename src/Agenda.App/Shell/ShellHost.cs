using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Agenda.App.Shell;

public sealed class ShellHost : IDisposable
{
    private const uint TrayMessage = NativeMethods.WmApp + 1;
    private const uint ClockSettingMessage = NativeMethods.WmApp + 2;
    private const uint TrayIconId = 1;
    private const int AgendaHotkeyId = 1;
    private const uint VirtualKeyA = 0x41;
    private const nuint ClockRefreshTimerId = 1;
    private const uint OpenMenuId = 1;
    private const uint SettingsMenuId = 2;
    private const uint ExitMenuId = 3;
    private const int NinSelect = NativeMethods.WmUser;
    private const int NinKeyselect = NativeMethods.WmUser + 1;

    private static readonly NativeMethods.WindowProcedure WindowProcedure = WindowProcedureThunk;

    private readonly nint _window;
    private readonly Action _toggle;
    private readonly Action _exit;
    private readonly Action _settings;
    private readonly Action _regionalChanged;
    private readonly Action? _dismiss;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _messageThread;
    private readonly string _windowClassName = $"Agenda.ShellHost.{Guid.NewGuid():N}";
    private readonly NativeMethods.LowLevelMouseProcedure _mouseProcedure;
    private readonly ClockClickGate _clickGate = new();
    private readonly object _clockSync = new();

    private nint _module;
    private nint _messageWindow;
    private nint _mouseHook;
    private nint _powerNotification;
    private nint _trayIcon;
    private uint _taskbarCreatedMessage;
    private ClockTarget[] _clockTargets = [];
    private Exception? _startupError;
    private GCHandle _selfHandle;
    private volatile bool _clockRequested;
    private string _clockStatus = "Off";
    private string _hotkeyStatus = "Unavailable";
    private bool _ownsTrayIcon;
    private bool _trayLightTheme;
    private bool _hotkeyRegistered;
    private bool _trayVersion4;
    private int _disposed;

    public ShellHost(
        nint window,
        Action toggle,
        Action exit,
        Action settings,
        Action regionalChanged,
        Action? dismiss = null)
    {
        if (window == 0)
            throw new ArgumentException("A valid flyout window handle is required.", nameof(window));

        _window = window;
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _regionalChanged = regionalChanged ?? throw new ArgumentNullException(nameof(regionalChanged));
        _dismiss = dismiss;
        _mouseProcedure = MouseHookProcedure;

        _messageThread = new Thread(MessageThreadMain)
        {
            IsBackground = true,
            Name = "Agenda shell host"
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The Agenda shell message window did not start.");
        if (_startupError is not null)
            throw new InvalidOperationException("The Agenda shell message window could not start.", _startupError);
    }

    public string ClockIntegrationStatus
    {
        get
        {
            lock (_clockSync)
                return _clockStatus;
        }
    }

    public string ClockSupportStatus => ClockIntegrationStatus;
    public bool IsVisible => NativeMethods.IsWindowVisible(_window);

    public bool IsTrayInteraction
    {
        get
        {
            if(_messageWindow==0 || Volatile.Read(ref _disposed)!=0 || !NativeMethods.GetCursorPos(out var point))return false;
            var identifier=new NativeMethods.NotifyIconIdentifier {
                Size=(uint)Marshal.SizeOf<NativeMethods.NotifyIconIdentifier>(),Window=_messageWindow,Id=TrayIconId
            };
            if(NativeMethods.ShellNotifyIconGetRect(ref identifier,out var bounds)!=0)return false;
            var swapped=NativeMethods.GetSystemMetrics(NativeMethods.SmSwapbutton)!=0;
            var primaryDown=NativeMethods.GetAsyncKeyState(swapped?2:1)<0;
            var secondaryDown=NativeMethods.GetAsyncKeyState(swapped?1:2)<0 || NativeMethods.GetAsyncKeyState(4)<0;
            var foreground=NativeMethods.GetForegroundWindow();
            var trayFocused=IsSupportedTaskbar(foreground) || GetClassName(foreground) is "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland";
            return ShellGeometry.ShouldDeferTrayDismissal(bounds.ToNative(),new NativePoint(point.X,point.Y),primaryDown,trayFocused,secondaryDown);
        }
    }

    public string HotkeyStatus
    {
        get
        {
            lock (_clockSync)
                return _hotkeyStatus;
        }
    }

    public void SetClockIntegration(bool enabled)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        // Clock interception is disabled in this build, including requests from
        // older saved preferences. Keep the implementation for a future release.
        _clockRequested = false;
        SetClockStatus("Off");
        NativeMethods.PostMessage(_messageWindow, ClockSettingMessage, 0, 0);
    }

    public void PositionFlyout(int widthDip, int heightDip)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (widthDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthDip));
        if (heightDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightDip));

        var monitor = ResolveActivationMonitor();
        var info = new NativeMethods.MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the activation monitor.");

        uint monitorDpi = 0;
        try
        {
            if (NativeMethods.GetDpiForMonitor(
                monitor, NativeMethods.MonitorDpiTypeEffective, out var dpiX, out _) == 0)
            {
                monitorDpi = dpiX;
            }
        }
        catch (DllNotFoundException)
        {
            // Windows versions without Shcore use the window/system fallbacks below.
        }
        catch (EntryPointNotFoundException)
        {
            // Windows versions without GetDpiForMonitor use the fallbacks below.
        }

        var dpi = ShellGeometry.SelectDpi(
            monitorDpi, NativeMethods.GetDpiForWindow(_window), NativeMethods.GetDpiForSystem());

        var width = ShellGeometry.DipsToPixels(widthDip, dpi);
        var height = ShellGeometry.DipsToPixels(heightDip, dpi);
        var gap = ShellGeometry.DipsToPixels(8, dpi);
        var position = ShellGeometry.PlaceFlyout(
            info.Work.ToNative(), info.Monitor.ToNative(), width, height, gap);

        var fittedWidth = Math.Min(width, info.Work.ToNative().Width);
        var fittedHeight = Math.Min(height, info.Work.ToNative().Height);
        if (!NativeMethods.SetWindowPos(
            _window, NativeMethods.HwndTopmost,
            position.X, position.Y, fittedWidth, fittedHeight,
            NativeMethods.SwpNoactivate | NativeMethods.SwpNoownerzorder))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the Agenda flyout.");
        }
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        NativeMethods.ShowWindow(_window, NativeMethods.SwShownormal);
        NativeMethods.SetForegroundWindow(_window);
    }

    public void Hide()
    {
        if (Volatile.Read(ref _disposed) == 0)
            NativeMethods.ShowWindow(_window, NativeMethods.SwHide);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var hostWindow = _messageWindow;
        if (hostWindow != 0)
            NativeMethods.PostMessage(hostWindow, NativeMethods.WmClose, 0, 0);

        if (Environment.CurrentManagedThreadId != _messageThread.ManagedThreadId)
            _messageThread.Join(TimeSpan.FromSeconds(5));

        _ready.Dispose();
    }

    private void MessageThreadMain()
    {
        try
        {
            _module = NativeMethods.GetModuleHandle(null);
            _selfHandle = GCHandle.Alloc(this);
            var windowClass = new NativeMethods.WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
                Instance = _module,
                Icon = NativeMethods.LoadIcon(0, NativeMethods.IdiApplication),
                ClassName = _windowClassName
            };

            if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the shell window class.");

            _messageWindow = NativeMethods.CreateWindowEx(
                0x00000080, _windowClassName, "Agenda shell host", 0,
                0, 0, 0, 0, 0, 0, _module, GCHandle.ToIntPtr(_selfHandle));
            if (_messageWindow == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the shell message window.");

            _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            _powerNotification = NativeMethods.RegisterSuspendResumeNotification(
                _messageWindow, NativeMethods.DeviceNotifyWindowHandle);
            LoadTrayIcon();
            AddTrayIcon();
            RegisterAgendaHotkey();
            _ready.Set();

            while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception error)
        {
            _startupError = error;
            _ready.Set();
        }
        finally
        {
            DisableClockIntegration();
            UnregisterAgendaHotkey();
            RemoveTrayIcon();
            if (_powerNotification != 0)
                NativeMethods.UnregisterSuspendResumeNotification(_powerNotification);
            if (_messageWindow != 0 && NativeMethods.IsWindow(_messageWindow))
                NativeMethods.DestroyWindow(_messageWindow);
            if (_module != 0)
                NativeMethods.UnregisterClass(_windowClassName, _module);
            if (_ownsTrayIcon && _trayIcon != 0)
                NativeMethods.DestroyIcon(_trayIcon);
            if (_selfHandle.IsAllocated)
                _selfHandle.Free();
            _messageWindow = 0;
        }
    }

    private static nint WindowProcedureThunk(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == NativeMethods.WmNccreate)
        {
            var create = Marshal.PtrToStructure<NativeMethods.CreateStruct>(lParam);
            NativeMethods.SetWindowLongPtr(window, NativeMethods.GwlpUserdata, create.CreateParams);
        }

        var state = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlpUserdata);
        if (state != 0)
        {
            var handle = GCHandle.FromIntPtr(state);
            if (handle.Target is ShellHost host)
                return host.WindowProcedureCore(window, message, wParam, lParam);
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    private nint WindowProcedureCore(nint window, uint message, nuint wParam, nint lParam)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            // An Explorer restart can also have changed the theme.
            RefreshTrayTheme();
            AddTrayIcon();
            RefreshClockIntegration();
            return 0;
        }

        switch (message)
        {
            case TrayMessage:
                HandleTrayMessage(unchecked((int)(long)lParam));
                return 0;
            case ClockSettingMessage:
                if (wParam != 0)
                    EnableClockIntegration();
                else
                    DisableClockIntegration();
                return 0;
            case NativeMethods.WmTimer when wParam == ClockRefreshTimerId:
                RefreshClockIntegration();
                return 0;
            case NativeMethods.WmHotkey when (int)wParam == AgendaHotkeyId:
                InvokeSafely(_toggle);
                return 0;
            case NativeMethods.WmDisplaychange:
                RefreshClockIntegration();
                return 0;
            case NativeMethods.WmSettingchange:
                InvokeSafely(_regionalChanged);
                RefreshTrayTheme();
                RefreshClockIntegration();
                return 0;
            case NativeMethods.WmTimechange:
                InvokeSafely(_regionalChanged);
                return 0;
            case NativeMethods.WmPowerbroadcast:
                if ((int)wParam == NativeMethods.PbtApmsuspend)
                    NativeMethods.ShowWindow(_window, NativeMethods.SwHide);
                else if ((int)wParam == NativeMethods.PbtApmresumeautomatic)
                {
                    AddTrayIcon();
                    RefreshClockIntegration();
                }
                return 1;
            case NativeMethods.WmClose:
                DisableClockIntegration();
                UnregisterAgendaHotkey();
                RemoveTrayIcon();
                NativeMethods.DestroyWindow(window);
                return 0;
            case NativeMethods.WmDestroy:
                NativeMethods.PostQuitMessage(0);
                return 0;
            default:
                return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }
    }

    private void AddTrayIcon()
    {
        if (_messageWindow == 0)
            return;

        var data = CreateNotifyIconData();
        NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data))
            return;

        data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        _trayVersion4 = NativeMethods.ShellNotifyIcon(NativeMethods.NimSetversion, ref data);
    }

    private void RemoveTrayIcon()
    {
        if (_messageWindow == 0)
            return;
        var data = CreateNotifyIconData();
        NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
    }

    private NativeMethods.NotifyIconData CreateNotifyIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        Window = _messageWindow,
        Id = TrayIconId,
        Flags = NativeMethods.NifMessage | NativeMethods.NifIcon |
                NativeMethods.NifTip | NativeMethods.NifShowtip,
        CallbackMessage = TrayMessage,
        Icon = _trayIcon != 0 ? _trayIcon : NativeMethods.LoadIcon(0, NativeMethods.IdiApplication),
        Tip = "Agenda",
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    // The notification area is painted in the system theme, which is a separate
    // setting from the one applied to application surfaces.
    private static bool SystemUsesLightTheme()
    {
        uint size = sizeof(uint);
        var status = NativeMethods.RegGetValue(
            NativeMethods.HkeyCurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme",
            NativeMethods.RrfRtRegDword,
            0,
            out var value,
            ref size);
        return status == 0 && value != 0;
    }

    private static nint LoadThemedTrayIcon(bool lightTheme)
    {
        // A light taskbar needs the dark ink and a dark taskbar the light ink.
        var file = lightTheme ? "TrayLight.ico" : "TrayDark.ico";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", file);
        if (!File.Exists(path))
            return 0;

        // Requesting the notification-area size makes Windows select that entry
        // from the multi-resolution file instead of resampling a larger one.
        return NativeMethods.LoadImage(
            0,
            path,
            NativeMethods.ImageIcon,
            NativeMethods.GetSystemMetrics(NativeMethods.SmCxsmicon),
            NativeMethods.GetSystemMetrics(NativeMethods.SmCysmicon),
            NativeMethods.LrLoadfromfile);
    }

    private void LoadTrayIcon()
    {
        _trayLightTheme = SystemUsesLightTheme();
        _trayIcon = LoadThemedTrayIcon(_trayLightTheme);
        if (_trayIcon != 0)
        {
            _ownsTrayIcon = true;
            return;
        }

        // Without the themed files, fall back to the executable's own icon.
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            var large = new nint[1];
            var small = new nint[1];
            if (NativeMethods.ExtractIconEx(executable, 0, large, small, 1) != 0)
            {
                _trayIcon = small[0] != 0 ? small[0] : large[0];
                _ownsTrayIcon = _trayIcon != 0;
                var unused = _trayIcon == small[0] ? large[0] : small[0];
                if (unused != 0 && unused != _trayIcon)
                    NativeMethods.DestroyIcon(unused);
            }
        }

        if (_trayIcon == 0)
            _trayIcon = NativeMethods.LoadIcon(0, NativeMethods.IdiApplication);
    }

    private void RefreshTrayTheme()
    {
        if (_messageWindow == 0)
            return;

        var lightTheme = SystemUsesLightTheme();
        if (lightTheme == _trayLightTheme)
            return;

        var replacement = LoadThemedTrayIcon(lightTheme);
        _trayLightTheme = lightTheme;
        if (replacement == 0)
            return;

        var previous = _trayIcon;
        var previousOwned = _ownsTrayIcon;
        _trayIcon = replacement;
        _ownsTrayIcon = true;

        var data = CreateNotifyIconData();
        NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
        if (previousOwned && previous != 0)
            NativeMethods.DestroyIcon(previous);
    }

    private void RegisterAgendaHotkey()
    {
        _hotkeyRegistered = NativeMethods.RegisterHotKey(
            _messageWindow, AgendaHotkeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNorepeat,
            VirtualKeyA);
        lock (_clockSync)
        {
            _hotkeyStatus = _hotkeyRegistered
                ? "Active — Ctrl+Alt+A toggles Agenda."
                : "Unavailable — Ctrl+Alt+A is already registered by another application.";
        }
    }

    private void UnregisterAgendaHotkey()
    {
        if (!_hotkeyRegistered)
            return;
        NativeMethods.UnregisterHotKey(_messageWindow, AgendaHotkeyId);
        _hotkeyRegistered = false;
    }

    private void HandleTrayMessage(int message)
    {
        var notification = message & 0xffff;
        switch (notification)
        {
            // Version 4 sends semantic activation as well as mouse messages.
            // Handling both toggles twice for a single click.
            case NativeMethods.WmLbuttonup when !_trayVersion4:
            case NinSelect when _trayVersion4:
            case NinKeyselect when _trayVersion4:
                InvokeSafely(_toggle);
                break;
            case NativeMethods.WmRbuttonup when !_trayVersion4:
            case NativeMethods.WmContextmenu when _trayVersion4:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        if(_dismiss is not null)InvokeSafely(_dismiss);
        if (!NativeMethods.GetCursorPos(out var point))
            return;

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
            return;

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, OpenMenuId, "Open Agenda");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, SettingsMenuId, "Settings");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ExitMenuId, "Exit");
            NativeMethods.SetForegroundWindow(_messageWindow);

            var command = NativeMethods.TrackPopupMenu(
                menu, NativeMethods.TpmReturncmd | NativeMethods.TpmNonotify,
                point.X, point.Y, 0, _messageWindow, 0);
            if (command == OpenMenuId)
                InvokeSafely(_toggle);
            else if (command == SettingsMenuId)
                InvokeSafely(_settings);
            else if (command == ExitMenuId)
                InvokeSafely(_exit);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private nint ResolveActivationMonitor()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            var hit = NativeMethods.WindowFromPoint(point);
            var root = NativeMethods.GetAncestor(hit, NativeMethods.GaRoot);
            if (IsSupportedTaskbar(root))
                return NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaulttonearest);
        }

        return NativeMethods.MonitorFromWindow(_window, NativeMethods.MonitorDefaulttonearest);
    }

    private void EnableClockIntegration()
    {
        if (!_clockRequested)
            return;
        NativeMethods.SetTimer(_messageWindow, ClockRefreshTimerId, 10_000, 0);
        RefreshClockIntegration();
    }

    private void DisableClockIntegration()
    {
        if (_messageWindow != 0)
            NativeMethods.KillTimer(_messageWindow, ClockRefreshTimerId);
        if (_mouseHook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
        Volatile.Write(ref _clockTargets, []);
        SetClockStatus("Off");
    }

    private void RefreshClockIntegration()
    {
        if (!_clockRequested)
            return;

        var targets = DiscoverClockTargets();
        Volatile.Write(ref _clockTargets, targets);
        if (targets.Length == 0)
        {
            if (_mouseHook != 0)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = 0;
            }
            SetClockStatus("Unavailable — no independently bounded Win32 clock target was found; Windows keeps handling clock clicks.");
            return;
        }

        if (_mouseHook == 0)
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouseProcedure, _module, 0);

        SetClockStatus(_mouseHook != 0
            ? "Active — compatible taskbar clock target discovered."
            : "Unavailable — the mouse hook could not be installed; Windows keeps handling clock clicks.");
    }

    private ClockTarget[] DiscoverClockTargets()
    {
        var targets = new List<ClockTarget>();
        NativeMethods.EnumWindows((taskbar, _) =>
        {
            if (!IsSupportedTaskbar(taskbar) || !NativeMethods.IsWindowVisible(taskbar) ||
                !NativeMethods.GetWindowRect(taskbar, out var taskbarRectangle))
            {
                return true;
            }

            NativeMethods.EnumChildWindows(taskbar, (candidate, _) =>
            {
                if (!string.Equals(GetClassName(candidate), "TrayClockWClass", StringComparison.Ordinal) ||
                    !NativeMethods.IsWindowVisible(candidate) ||
                    NativeMethods.GetAncestor(candidate, NativeMethods.GaRoot) != taskbar ||
                    !NativeMethods.GetWindowRect(candidate, out var clockRectangle))
                {
                    return true;
                }

                var dpi = NativeMethods.GetDpiForWindow(candidate);
                if (dpi == 0)
                    dpi = 96;
                if (ClockTarget.TryCreate(
                    taskbar, candidate, taskbarRectangle.ToNative(), clockRectangle.ToNative(), dpi,
                    out var target))
                {
                    targets.Add(target);
                }
                return true;
            }, 0);
            return true;
        }, 0);
        return [.. targets];
    }

    private nint MouseHookProcedure(int code, nuint wParam, nint lParam)
    {
        if (code < 0)
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);

        var hookData = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);
        if ((hookData.Flags & NativeMethods.LlmhfInjected) != 0)
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);

        var point = new NativePoint(hookData.Point.X, hookData.Point.Y);
        var decision = _clickGate.Handle(unchecked((int)wParam), point, IsValidatedClockPoint);
        if (decision == ClockMouseDecision.Activate)
            InvokeSafely(_toggle);

        return decision == ClockMouseDecision.PassThrough
            ? NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam)
            : 1;
    }

    private bool IsValidatedClockPoint(NativePoint point)
    {
        foreach (var cached in Volatile.Read(ref _clockTargets))
        {
            if (!cached.Contains(point) || !NativeMethods.IsWindow(cached.ClockWindow) ||
                !string.Equals(GetClassName(cached.ClockWindow), "TrayClockWClass", StringComparison.Ordinal) ||
                NativeMethods.GetAncestor(cached.ClockWindow, NativeMethods.GaRoot) != cached.TaskbarWindow ||
                !IsSupportedTaskbar(cached.TaskbarWindow) ||
                !NativeMethods.GetWindowRect(cached.TaskbarWindow, out var taskbarRectangle) ||
                !NativeMethods.GetWindowRect(cached.ClockWindow, out var clockRectangle))
            {
                continue;
            }

            var dpi = NativeMethods.GetDpiForWindow(cached.ClockWindow);
            if (dpi == 0)
                dpi = 96;
            if (!ClockTarget.TryCreate(
                cached.TaskbarWindow, cached.ClockWindow,
                taskbarRectangle.ToNative(), clockRectangle.ToNative(), dpi, out var live) ||
                !live.Contains(point))
            {
                continue;
            }

            var nativePoint = new NativeMethods.Point { X = point.X, Y = point.Y };
            var hit = NativeMethods.WindowFromPoint(nativePoint);
            if (hit == live.ClockWindow || NativeMethods.IsChild(live.ClockWindow, hit))
                return true;
        }
        return false;
    }

    private static bool IsSupportedTaskbar(nint window)
    {
        if (window == 0)
            return false;
        var className = GetClassName(window);
        return string.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal) ||
               string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.Ordinal);
    }

    private static string GetClassName(nint window)
    {
        var buffer = new StringBuilder(128);
        return NativeMethods.GetClassName(window, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    private void SetClockStatus(string status)
    {
        lock (_clockSync)
            _clockStatus = status;
    }

    private static void InvokeSafely(Action callback)
    {
        try
        {
            callback();
        }
        catch
        {
            // Keep the notification and hook message pump alive. The controller owns
            // UI dispatch and its own error reporting.
        }
    }
}
