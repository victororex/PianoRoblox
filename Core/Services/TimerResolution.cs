using System.Runtime.InteropServices;

namespace PianoRoblox.Core.Services;

internal sealed class TimerResolution : IDisposable
{
    private readonly bool acquired = OperatingSystem.IsWindows() && timeBeginPeriod(1) == 0;
    public void Dispose() { if (acquired) timeEndPeriod(1); }
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}

