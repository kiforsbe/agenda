namespace Agenda.App.Shell;

internal readonly record struct NativePoint(int X, int Y);

internal readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Math.Max(0, Right - Left);
    internal int Height => Math.Max(0, Bottom - Top);

    internal bool Contains(NativePoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    internal bool Contains(NativeRect rectangle) =>
        rectangle.Left >= Left && rectangle.Top >= Top &&
        rectangle.Right <= Right && rectangle.Bottom <= Bottom;
}

internal static class ShellGeometry
{
    internal static bool ShouldDeferTrayDismissal(NativeRect? iconBounds, NativePoint pointer,
        bool primaryDown, bool trayFocused, bool secondaryDown) =>
        !secondaryDown && (primaryDown || trayFocused) && iconBounds is { } bounds && bounds.Contains(pointer);

    internal static uint SelectDpi(uint monitorDpi, uint windowDpi, uint systemDpi) =>
        monitorDpi != 0 ? monitorDpi : windowDpi != 0 ? windowDpi : systemDpi != 0 ? systemDpi : 96;

    internal static int DipsToPixels(int dips, uint dpi) =>
        checked((int)Math.Round(dips * dpi / 96d, MidpointRounding.AwayFromZero));

    internal static NativePoint PlaceFlyout(
        NativeRect workArea,
        NativeRect monitorArea,
        int widthPixels,
        int heightPixels,
        int gapPixels)
    {
        var width = Math.Min(Math.Max(0, widthPixels), workArea.Width);
        var height = Math.Min(Math.Max(0, heightPixels), workArea.Height);
        var gap = Math.Max(0, gapPixels);

        int x;
        int y;

        if (workArea.Left > monitorArea.Left)
        {
            x = workArea.Left + gap;
            y = workArea.Bottom - height - gap;
        }
        else if (workArea.Right < monitorArea.Right)
        {
            x = workArea.Right - width - gap;
            y = workArea.Bottom - height - gap;
        }
        else if (workArea.Top > monitorArea.Top)
        {
            x = workArea.Right - width - gap;
            y = workArea.Top + gap;
        }
        else
        {
            x = workArea.Right - width - gap;
            y = workArea.Bottom - height - gap;
        }

        var maxX = workArea.Right - width;
        var maxY = workArea.Bottom - height;
        return new NativePoint(
            Math.Clamp(x, workArea.Left, maxX),
            Math.Clamp(y, workArea.Top, maxY));
    }
}

internal readonly record struct ClockTarget(
    nint TaskbarWindow,
    nint ClockWindow,
    NativeRect TaskbarBounds,
    NativeRect ClockBounds)
{
    internal bool Contains(NativePoint point) => ClockBounds.Contains(point);

    internal static bool TryCreate(
        nint taskbarWindow,
        nint clockWindow,
        NativeRect taskbarBounds,
        NativeRect clockBounds,
        uint dpi,
        out ClockTarget target)
    {
        target = default;
        if (taskbarWindow == 0 || clockWindow == 0 || dpi == 0 ||
            taskbarBounds.Width == 0 || taskbarBounds.Height == 0 ||
            !taskbarBounds.Contains(clockBounds))
        {
            return false;
        }

        var minimum = ShellGeometry.DipsToPixels(16, dpi);
        var maximumWidth = ShellGeometry.DipsToPixels(240, dpi);
        var maximumHeight = ShellGeometry.DipsToPixels(120, dpi);
        if (clockBounds.Width < minimum || clockBounds.Height < minimum ||
            clockBounds.Width > maximumWidth || clockBounds.Height > maximumHeight)
        {
            return false;
        }

        var taskbarArea = (long)taskbarBounds.Width * taskbarBounds.Height;
        var clockArea = (long)clockBounds.Width * clockBounds.Height;
        if (clockArea * 4 > taskbarArea)
            return false;

        target = new ClockTarget(taskbarWindow, clockWindow, taskbarBounds, clockBounds);
        return true;
    }
}

internal enum ClockMouseDecision
{
    PassThrough,
    Swallow,
    Activate
}

internal sealed class ClockClickGate
{
    private const int LeftButtonDown = 0x0201;
    private const int LeftButtonUp = 0x0202;
    private bool _capturing;

    internal ClockMouseDecision Handle(
        int message,
        NativePoint point,
        Func<NativePoint, bool> isValidatedClockPoint)
    {
        if (message == LeftButtonDown)
        {
            _capturing = isValidatedClockPoint(point);
            return _capturing ? ClockMouseDecision.Swallow : ClockMouseDecision.PassThrough;
        }

        if (message != LeftButtonUp || !_capturing)
            return ClockMouseDecision.PassThrough;

        _capturing = false;
        return isValidatedClockPoint(point)
            ? ClockMouseDecision.Activate
            : ClockMouseDecision.Swallow;
    }
}
