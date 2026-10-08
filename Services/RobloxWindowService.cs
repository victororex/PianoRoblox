using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PianoRoblox.Services;

public sealed class RobloxWindowService
{
    public bool IsFocused()
    {
        try
        {
            nint window = GetForegroundWindow();
            if (window == 0) return false;
            GetWindowThreadProcessId(window, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName, "RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}

