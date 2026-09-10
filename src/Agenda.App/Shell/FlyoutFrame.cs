using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Agenda.App.Shell;

public static class FlyoutFrame
{
    public static IDisposable Attach(nint window) => new ClientArea(window);

    public static void Apply(nint window, bool dark)
    {
        uint theme=dark?1u:0u, corners=2, border=0xFFFFFFFE;
        NativeMethods.DwmSetWindowAttribute(window,20,ref theme,sizeof(uint));
        NativeMethods.DwmSetWindowAttribute(window,33,ref corners,sizeof(uint));
        NativeMethods.DwmSetWindowAttribute(window,34,ref border,sizeof(uint));
    }

    // A fixed-size WinUI presenter can retain WS_DLGFRAME even with HasBorder=false.
    // Own the non-client calculation rather than fighting cached presenter styles.
    // Only the application's own window is subclassed, on its UI thread.
    private sealed class ClientArea : IDisposable
    {
        private const nuint Id=0xA6DA;
        private readonly NativeMethods.SubclassProcedure _callback;
        private nint _window;

        public ClientArea(nint window)
        {
            _window=window;_callback=HandleMessage;
            if(!NativeMethods.SetWindowSubclass(window,_callback,Id,0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not configure the flyout frame.");
            NativeMethods.SetWindowPos(window,0,0,0,0,0,0x0001|0x0002|0x0004|0x0010|0x0020);
        }

        private nint HandleMessage(nint window,uint message,nuint wParam,nint lParam,nuint id,nuint data)
        {
            if(message==0x0083) return 0; // WM_NCCALCSIZE: all of the window is client content.
            if(message==0x0082) Dispose(); // WM_NCDESTROY
            return NativeMethods.DefSubclassProc(window,message,wParam,lParam);
        }

        public void Dispose()
        {
            if(_window==0)return;
            NativeMethods.RemoveWindowSubclass(_window,_callback,Id);_window=0;
        }
    }
}
