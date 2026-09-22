using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Scrunch.ScrunchFX;

// A single non-activating, click-through, alpha-composited HWND. Physical pixel
// placement is independent of XAML layout and supports negative monitor origins.
// DirectComposition's swap-chain host works on Scrunch's Windows 10 baseline;
// lifted WinUI 1.8 ICompositorInterop only exposes CreateGraphicsDevice.
internal sealed partial class FxOverlay : IDisposable
{
    private static readonly WindowProc Procedure = WndProc;
    private static ushort _class;
    public IntPtr Handle { get; private set; }
    public FxOverlay()
    {
        if (_class == 0)
        {
            var wc = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(Procedure), Instance = GetModuleHandle(null), Name = "ScrunchFXOverlay" };
            _class = RegisterClassEx(ref wc);
            if (_class == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Handle = CreateWindowEx(0x00200000 | 0x08000000 | 0x80 | 0x20, "ScrunchFXOverlay", "ScrunchFX paper", 0x80000000, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Position(int x, int y, int width, int height)
    {
        if (!SetWindowPos(Handle, new IntPtr(-1), x, y, width, height, 0x0010)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Show() => ShowWindow(Handle, 8);
    public void Hide() => ShowWindow(Handle, 0);
    public void Dispose() { if (Handle != IntPtr.Zero) { DestroyWindow(Handle); Handle = IntPtr.Zero; } }
    private static IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l) => m switch
    {
        0x0084 => new IntPtr(-1), // HTTRANSPARENT
        0x0021 => new IntPtr(3),  // MA_NOACTIVATE
        0x0014 => new IntPtr(1),
        _ => DefWindowProc(h, m, w, l)
    };
    private delegate IntPtr WindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint Size, Style; public IntPtr Procedure; public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background; public string? Menu, Name; public IntPtr SmallIcon;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
}
