using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Noot_Proto;

internal enum TrayCommand { NewNote = 1, ShowNotes, Undo, Settings, Quit }

// Owned by the long-lived shell HWND. No polling, hidden helper process or tray library.
internal sealed class TrayIcon : IDisposable
{
    internal static readonly Guid IconGuid = TrayIdentity.ForExecutable(Environment.ProcessPath!);
    internal const uint CallbackMessage = 0x8000 + 91;
    private readonly IntPtr _window;
    private readonly SubclassProc _callback;
    private readonly DispatcherQueue _queue;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Action _toggle;
    private readonly Action<TrayCommand> _command;
    private readonly Func<bool> _canUndo;
    private IntPtr _icon;
    private bool _disposed;
    public bool Registered { get; private set; }
    public bool MenuOpen { get; private set; }
    public event Action? RegistrationChanged;
    public event Action? MenuClosed;

    public TrayIcon(IntPtr window, Action toggle, Action<TrayCommand> command, Func<bool> canUndo)
    {
        _window = window; _toggle = toggle; _command = command; _canUndo = canUndo;
        _queue = DispatcherQueue.GetForCurrentThread(); _callback = WindowProc;
        if (!SetWindowSubclass(window, _callback, 73, 0)) return;
        _icon = LoadTrayImage();
        Restore();
    }

    private IntPtr LoadTrayImage()
    {
        var anchor = Rect.From(Anchor());
        uint dpi = 96;
        if (GetDpiForMonitor(MonitorFromRect(ref anchor, 2), 0, out uint x, out _) == 0) dpi = x;
        // Select the ICO frame for the tray's monitor, independently of the shell's DPI.
        return LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "Scrunch.ico"), 1,
            GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi), 0x10);
    }

    private void RefreshImage()
    {
        if (_disposed || !Registered) return;
        var replacement = LoadTrayImage();
        if (replacement == IntPtr.Zero) return;
        var data = Data(); data.Icon = replacement;
        if (Shell_NotifyIcon(1, ref data))
        { DestroyIcon(_icon); _icon = replacement; }
        else DestroyIcon(replacement);
    }

    private IconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<IconData>(), Window = _window, Id = 1,
        Flags = 1 | 2 | 4 | 0x20 | 0x80, Callback = CallbackMessage, Icon = _icon,
        Tip = "Scrunch", Info = "", InfoTitle = "", Guid = IconGuid
    };

    private void Restore()
    {
        if (_disposed) return;
        var data = Data();
        // A synthetic TaskbarCreated can arrive while the icon still exists.
        if (Registered) Shell_NotifyIcon(2, ref data);
        Registered = _icon != IntPtr.Zero && Shell_NotifyIcon(0, ref data);
        if (Registered)
        {
            data.Version = 4;
            if (!Shell_NotifyIcon(4, ref data))
            { Shell_NotifyIcon(2, ref data); Registered = false; }
        }
        if (Registered) RefreshImage();
        RegistrationChanged?.Invoke();
    }

    public DesktopRect Anchor()
    {
        var id = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = _window, Id = 1, Guid = IconGuid };
        if (Registered && Shell_NotifyIconGetRect(ref id, out var rect) == 0) return rect.Desktop;
        GetCursorPos(out var point);
        return new(point.X, point.Y, point.X + 1, point.Y + 1);
    }

    public bool IsLeftClickOnIcon()
    {
        GetCursorPos(out var point);
        var r = Anchor();
        return (GetAsyncKeyState(1) & 0x8000) != 0 && point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom;
    }

    public DesktopRect Position(int width, int height)
    {
        var anchor = Anchor();
        var exclusion = anchor;
        var point = new Point { X = anchor.Left + anchor.Width / 2, Y = anchor.Top + anchor.Height / 2 };
        var root = GetAncestor(WindowFromPoint(point), 2);
        var className = new System.Text.StringBuilder(128);
        GetClassName(root, className, className.Capacity);
        // Explorer's overflow may remain visible during activation. Keep the whole
        // shell clear of that native surface, not just the individual icon cell.
        if (className.ToString() == "NotifyIconOverflowWindow" && GetWindowRect(root, out var overflow))
            exclusion = overflow.Desktop;
        return PositionNear(anchor, width, height, exclusion);
    }

    internal static DesktopRect PositionNear(DesktopRect anchor, int width, int height, DesktopRect? exclusion = null)
    {
        var native = Rect.From(anchor);
        var monitor = MonitorFromRect(ref native, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return new(anchor.Left - width, anchor.Top - height, anchor.Left, anchor.Top);
        var fallback = TrayPlacement.Place(anchor, info.Work.Desktop, width, height);
        var point = new Point { X = anchor.Left + anchor.Width / 2, Y = anchor.Top + anchor.Height / 2 };
        var size = new Size { Width = width, Height = height };
        var exclude = Rect.From(exclusion ?? anchor);
        // Exclude the icon; Windows chooses the side with space and honors its work area.
        if (CalculatePopupWindowPosition(ref point, ref size, 0x10000 | 0x4 | 0x10, ref exclude, out var result))
            return TrayPlacement.Clamp(result.Desktop, info.Work.Desktop);
        return fallback;
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (message == _taskbarCreated && _taskbarCreated != 0)
            _queue.TryEnqueue(Restore);
        else if (message is 0x7e or 0x1a) // display configuration / system settings changed
            _queue.TryEnqueue(RefreshImage);
        else if (message == CallbackMessage)
        {
            int notification = (int)(lParam.ToInt64() & 0xffff);
            // Version 4 uses NIN_SELECT / NIN_KEYSELECT, not WM_LBUTTONUP.
            if (notification is 0x400 or 0x401) _queue.TryEnqueue(() => { if (!_disposed) _toggle(); });
            else if (notification == 0x7b) _queue.TryEnqueue(ShowMenu);
            return IntPtr.Zero;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        if (_disposed || MenuOpen) return;
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        uint selected = 0;
        MenuOpen = true;
        try
        {
            AppendMenu(menu, 0, 1, "&New note"); AppendMenu(menu, 0, 2, "&Show notes");
            AppendMenu(menu, _canUndo() ? 0u : 1u, 3, "&Undo last discard");
            AppendMenu(menu, 0x800, 0, null);
            AppendMenu(menu, 0, 4, "S&ettings"); AppendMenu(menu, 0, 5, "&Quit");
            var anchor = Anchor();
            SetForegroundWindow(_window);
            selected = TrackPopupMenuEx(menu, 0x100 | 0x2, anchor.Left, anchor.Top, _window, IntPtr.Zero);
            PostMessage(_window, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); MenuOpen = false; }
        if (selected is >= 1 and <= 5) _command((TrayCommand)selected);
        MenuClosed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = Data();
        if (Registered) Shell_NotifyIcon(2, ref data);
        Registered = false;
        RemoveWindowSubclass(_window, _callback, 73);
        if (_icon != IntPtr.Zero) { DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct IconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly DesktopRect Desktop => new(Left, Top, Right, Bottom);
        public static Rect From(DesktopRect r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref IconData data);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier id, out Rect rect);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool CalculatePopupWindowPosition(ref Point point, ref Size size, uint flags, ref Rect exclude, out Rect popup);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string? label);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
