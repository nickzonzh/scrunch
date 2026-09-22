using System;
using System.Runtime.InteropServices;

namespace Scrunch;

// DWM needs both an alpha backdrop and a cleared Win32 background on Windows 10.
// Merely setting a transparent XAML root leaves the default white HWND surface.
internal sealed class NativeTransparency : IDisposable
{
    private readonly IntPtr _window;
    private readonly SubclassProc _callback;
    public NativeTransparency(IntPtr window)
    {
        _window = window;
        _callback = WindowProc;
        if (!SetWindowSubclass(window, _callback, 71, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        // A transparent note is a popup surface, not a decorated overlapped window.
        // Keep DWM composition enabled: disabling it would break per-pixel alpha.
        SetWindowLong(window, -16, (GetWindowLong(window, -16) & ~0x00C40000) | unchecked((int)0x80000000));
        SetWindowLong(window, -20, GetWindowLong(window, -20) & ~0x00020301);
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        Configure();
        var dc = GetDC(window);
        if (dc != IntPtr.Zero) { Paint(dc); ReleaseDC(window, dc); }
    }
    private void Configure()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            uint none = 0xFFFFFFFE; // DWMWA_COLOR_NONE (Windows 11 only)
            DwmSetWindowAttribute(_window, 34, ref none, sizeof(uint));
        }
        var margins = new Margins();
        Marshal.ThrowExceptionForHR(DwmExtendFrameIntoClientArea(_window, ref margins));
        var region = CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new Blur { Flags = 3, Enable = true, Region = region };
            Marshal.ThrowExceptionForHR(DwmEnableBlurBehindWindow(_window, ref blur));
        }
        finally { DeleteObject(region); }
    }
    private void Paint(IntPtr dc)
    {
        if (GetClientRect(_window, out var rect)) FillRect(dc, ref rect, GetStockObject(4));
    }
    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (message == 0x0083) return IntPtr.Zero; // WM_NCCALCSIZE: all pixels are client area
        if (message == 0x0085) return IntPtr.Zero; // WM_NCPAINT: paper draws its own edges
        if (message == 0x0086) return new IntPtr(1); // WM_NCACTIVATE: no activation outline
        if (message == 0x0014) { Paint(wParam); return new IntPtr(1); }
        if (message == 0x031E)
        {
            // A composition reset must not allow an exception across the native callback.
            try { Configure(); } catch (COMException) { }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    public void Dispose() => RemoveWindowSubclass(_window, _callback, 71);
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Blur
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)] public bool Transition;
    }
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(IntPtr window, ref Blur blur);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int id);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref uint value, int size);
}
