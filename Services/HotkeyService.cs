using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PianoRoblox.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source;
    private readonly Dictionary<int, string> actions = new();
    public event Action<string>? Pressed;
    public HotkeyService(nint handle, IReadOnlyDictionary<string, int> hotkeys, Action<string> log)
    {
        source = HwndSource.FromHwnd(handle); source.AddHook(Hook);
        int id = 1;
        foreach (var key in hotkeys)
        {
            if (RegisterHotKey(handle, id, 0x4000, (uint)key.Value)) actions[id] = key.Key;
            else log($"Atalho {key.Key} indisponível; pode estar em uso por outro app.");
            id++;
        }
    }
    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x312 && actions.TryGetValue((int)wParam, out var action)) { handled = true; Pressed?.Invoke(action); }
        return 0;
    }
    public void Dispose() { foreach (int id in actions.Keys) UnregisterHotKey(source.Handle, id); source.RemoveHook(Hook); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}

