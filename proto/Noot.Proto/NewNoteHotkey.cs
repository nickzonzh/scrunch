using System.Runtime.InteropServices;

namespace Noot_Proto;

internal sealed class NewNoteHotkey : IDisposable
{
    private const int Id = 0x4E4F;
    private readonly IntPtr _window;
    private readonly SubclassProc _callback;
    private readonly Action _create;
    public bool Registered { get; }
    public NewNoteHotkey(IntPtr window, Action create)
    {
        _window = window; _create = create; _callback = WindowProc;
        if (!SetWindowSubclass(window, _callback, 72, 0)) return;
        // Ctrl + Alt + N; MOD_NOREPEAT prevents an accidental stream of notes.
        Registered = RegisterHotKey(window, Id, 0x4003, 0x4E);
    }
    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (message == 0x0312 && wParam.ToInt32() == Id)
        {
            // Dispatch managed work outside the unmanaged window callback.
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _create());
            return IntPtr.Zero;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    public void Dispose()
    {
        if (Registered) UnregisterHotKey(_window, Id);
        RemoveWindowSubclass(_window, _callback, 72);
    }
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
