using System.Runtime.InteropServices;
using PianoRoblox.Core.Models;

namespace PianoRoblox.Services;

public readonly record struct KeyInput(ushort Key, bool IsKeyUp = false);
public readonly record struct KeyboardInput(ushort VirtualKey, ushort ScanCode, uint Flags);

// Pure packet construction is separate from user32 so it can be tested without injection.
public static class KeyboardInputBuilder
{
    public static KeyboardInput Build(KeyInput input, InputMode mode, Func<ushort, ushort> mapScanCode) => new(
        mode == InputMode.VirtualKey ? input.Key : (ushort)0,
        mapScanCode(input.Key),
        (input.IsKeyUp ? 2u : 0u) | (mode == InputMode.ScanCode ? 8u : 0u));

    public static KeyboardInput[] BuildBatch(IEnumerable<KeyInput> inputs, InputMode mode,
        Func<ushort, ushort> mapScanCode) => inputs.Select(input => Build(input, mode, mapScanCode)).ToArray();
}

public interface IKeyboardInputTransport
{
    ushort MapScanCode(ushort virtualKey);
    uint Send(KeyboardInput[] inputs);
    int LastError { get; }
}

internal sealed class User32KeyboardInputTransport : IKeyboardInputTransport
{
    public int LastError { get; private set; }
    public ushort MapScanCode(ushort virtualKey) => (ushort)MapVirtualKey(virtualKey, 0);
    public uint Send(KeyboardInput[] inputs)
    {
        var native = inputs.Select(input => new INPUT
        {
            Type = 1,
            Data = new INPUTUNION { Keyboard = new KEYBDINPUT
            { VirtualKey = input.VirtualKey, ScanCode = input.ScanCode, Flags = input.Flags } }
        }).ToArray();
        uint sent = SendInput((uint)native.Length, native, Marshal.SizeOf<INPUT>());
        LastError = Marshal.GetLastWin32Error();
        return sent;
    }

    // MOUSEINPUT gives the union its required native size (INPUT is 40 bytes on x64).
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public INPUTUNION Data; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);
}
