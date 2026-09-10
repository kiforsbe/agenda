using Agenda.App.Shell;
using System.Runtime.InteropServices;

if (args.Contains("--smoke", StringComparer.Ordinal))
    return RunNativeSmokeProbe();

var checks = new (string Name, Action Body)[]
{
    ("tray click on an open flyout closes after deactivation", () =>
    {
        var visible=true;
        if(!ShellGeometry.ShouldDeferTrayDismissal(new NativeRect(100,100,116,116),new NativePoint(108,108),true,true,false))visible=false;
        var host=(ShellHost)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ShellHost));
        typeof(ShellHost).GetField("_toggle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(host,(Action)(()=>visible=!visible));
        typeof(ShellHost).GetField("_trayVersion4",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(host,true);
        typeof(ShellHost).GetMethod("HandleTrayMessage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(host,[0x00010400]);
        False(visible);
    }),
    ("tray dismissal deferral stays local to its primary interaction", () =>
    {
        var bounds=new NativeRect(100,100,116,116);var point=new NativePoint(108,108);
        True(ShellGeometry.ShouldDeferTrayDismissal(bounds,point,true,true,false));
        True(ShellGeometry.ShouldDeferTrayDismissal(bounds,point,false,true,false));
        False(ShellGeometry.ShouldDeferTrayDismissal(bounds,new NativePoint(116,108),true,true,false));
        False(ShellGeometry.ShouldDeferTrayDismissal(bounds,point,false,false,false));
        False(ShellGeometry.ShouldDeferTrayDismissal(bounds,point,false,true,true));
        False(ShellGeometry.ShouldDeferTrayDismissal(null,point,true,true,false));
    }),
    ("version 4 tray mouse notifications activate exactly once", () =>
    {
        var activations=0;
        // Exercise the actual callback without constructing a live notification icon.
        var host=(ShellHost)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ShellHost));
        typeof(ShellHost).GetField("_toggle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(host,(Action)(()=>activations++));
        typeof(ShellHost).GetField("_trayVersion4",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.SetValue(host,true);
        var handler=typeof(ShellHost).GetMethod("HandleTrayMessage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        handler.Invoke(host,[0x00010202]); // WM_LBUTTONUP, icon ID 1
        handler.Invoke(host,[0x00010400]); // NIN_SELECT, same click
        Equal(1,activations);
    }),
    ("bottom taskbar anchors above and right-aligns", () =>
        Equal(new NativePoint(1520, 412), ShellGeometry.PlaceFlyout(
            new NativeRect(0, 0, 1920, 1040), new NativeRect(0, 0, 1920, 1080), 392, 620, 8))),

    ("left taskbar anchors inside work area at lower left", () =>
        Equal(new NativePoint(82, 370), ShellGeometry.PlaceFlyout(
            new NativeRect(72, 0, 1920, 1080), new NativeRect(0, 0, 1920, 1080), 500, 700, 10))),

    ("oversized flyout is clamped to work-area origin", () =>
        Equal(new NativePoint(-1920, 40), ShellGeometry.PlaceFlyout(
            new NativeRect(-1920, 40, 0, 1040), new NativeRect(-1920, 0, 0, 1080), 2200, 1300, 8))),

    ("DIP dimensions round at target monitor DPI", () =>
    {
        Equal(588, ShellGeometry.DipsToPixels(392, 144));
        Equal(621, ShellGeometry.DipsToPixels(414, 144));
    }),

    ("target-monitor DPI wins with safe fallbacks", () =>
    {
        Equal((uint)168, ShellGeometry.SelectDpi(168, 144, 120));
        Equal((uint)144, ShellGeometry.SelectDpi(0, 144, 120));
        Equal((uint)120, ShellGeometry.SelectDpi(0, 0, 120));
        Equal((uint)96, ShellGeometry.SelectDpi(0, 0, 0));
    }),

    ("validated clock accepts only exact discovered bounds", () =>
    {
        var target = new ClockTarget(
            (nint)1, (nint)2,
            new NativeRect(0, 1040, 1920, 1080),
            new NativeRect(1760, 1040, 1840, 1080));
        True(target.Contains(new NativePoint(1800, 1060)));
        False(target.Contains(new NativePoint(1840, 1060)));
        False(target.Contains(new NativePoint(1900, 1060)));
    }),

    ("clock validation rejects broad and detached targets", () =>
    {
        False(ClockTarget.TryCreate(
            (nint)1, (nint)2,
            new NativeRect(0, 1040, 1920, 1080),
            new NativeRect(1500, 1040, 1920, 1080), 96, out _));
        False(ClockTarget.TryCreate(
            (nint)1, (nint)2,
            new NativeRect(0, 1040, 1920, 1080),
            new NativeRect(1760, 1000, 1840, 1040), 96, out _));
    }),

    ("validated left click activates on release", () =>
    {
        var gate = new ClockClickGate();
        var point = new NativePoint(100, 100);
        Equal(ClockMouseDecision.Swallow, gate.Handle(0x0201, point, _ => true));
        Equal(ClockMouseDecision.Activate, gate.Handle(0x0202, point, _ => true));
    }),

    ("unknown targets and right clicks pass through", () =>
    {
        var gate = new ClockClickGate();
        var point = new NativePoint(100, 100);
        Equal(ClockMouseDecision.PassThrough, gate.Handle(0x0201, point, _ => false));
        Equal(ClockMouseDecision.PassThrough, gate.Handle(0x0204, point, _ => true));
        Equal(ClockMouseDecision.PassThrough, gate.Handle(0x0205, point, _ => true));
    }),

    ("captured drag is swallowed without activation", () =>
    {
        var gate = new ClockClickGate();
        var point = new NativePoint(100, 100);
        Equal(ClockMouseDecision.Swallow, gate.Handle(0x0201, point, _ => true));
        Equal(ClockMouseDecision.Swallow, gate.Handle(0x0202, point, _ => false));
    })
};

var failures = 0;
foreach (var check in checks)
{
    try
    {
        check.Body();
        Console.WriteLine($"PASS {check.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {check.Name}: {error.Message}");
    }
}

if (failures != 0)
{
    Console.Error.WriteLine($"{failures} shell check(s) failed.");
    return 1;
}

Console.WriteLine($"{checks.Length} shell checks passed.");
return 0;

static int RunNativeSmokeProbe()
{
    NativeMethods.WindowProcedure procedure = (window, message, wParam, lParam) =>
        NativeMethods.DefWindowProc(window, message, wParam, lParam);
    var module = NativeMethods.GetModuleHandle(null);
    var className = $"Agenda.ShellSmoke.{Guid.NewGuid():N}";
    var windowClass = new NativeMethods.WindowClassEx
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
        WindowProcedure = Marshal.GetFunctionPointerForDelegate(procedure),
        Instance = module,
        ClassName = className
    };

    if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

    var window = NativeMethods.CreateWindowEx(
        0x00000080, className, "Agenda shell smoke target", 0x00400000,
        0, 0, 200, 200, 0, 0, module, 0);
    if (window == 0)
        throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

    try
    {
        using var frame=FlyoutFrame.Attach(window);
        FlyoutFrame.Apply(window,true);
        NativeMethods.GetWindowRect(window,out var outer);
        NativeMethods.GetClientRect(window,out var client);
        Equal(outer.Right-outer.Left,client.Right-client.Left);
        Equal(outer.Bottom-outer.Top,client.Bottom-client.Top);
        using var shell = new ShellHost(window, () => { }, () => { }, () => { }, () => { });
        Equal(IntPtr.Size==8?40:28,Marshal.SizeOf<NativeMethods.NotifyIconIdentifier>());
        _=shell.IsTrayInteraction; // Exercise the native icon-bounds query and input-state interop.
        shell.PositionFlyout(392, 620);
        shell.Show();
        shell.Hide();
        shell.SetClockIntegration(true);
        Thread.Sleep(250);
        Equal("Off", shell.ClockIntegrationStatus);
        Console.WriteLine($"Clock integration: {shell.ClockIntegrationStatus}");
        Console.WriteLine($"Global hotkey: {shell.HotkeyStatus}");
        shell.SetClockIntegration(false);
        Console.WriteLine("Native shell lifecycle smoke probe passed.");
        return 0;
    }
    finally
    {
        NativeMethods.DestroyWindow(window);
        NativeMethods.UnregisterClass(className, module);
        GC.KeepAlive(procedure);
    }
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, got {actual}");
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("expected true");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("expected false");
}
