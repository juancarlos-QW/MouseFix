using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MouseFix.App;

/// <summary>One relative mouse packet read straight from the sensor via Raw Input.</summary>
internal readonly record struct RawMousePacket(int Dx, int Dy, ushort ButtonFlags)
{
    public const ushort LeftDown = 0x0001;
    public const ushort LeftUp = 0x0002;
}

/// <summary>Raw Input registration and parsing shared by the test windows.</summary>
internal static class RawMouse
{
    public const int WM_INPUT = 0x00FF;

    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEMOUSE = 0;
    private const ushort MOUSE_MOVE_ABSOLUTE = 0x01;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint QS_RAWINPUT = 0x0400;

    /// <summary>Delivers WM_INPUT for the mouse to the window while it is in the foreground.</summary>
    public static void Register(nint hwnd)
    {
        // Usage page 1 / usage 2 = generic mouse.
        var device = new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x02, Flags = 0, Target = hwnd };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void Unregister()
    {
        var device = new RAWINPUTDEVICE { UsagePage = 0x01, Usage = 0x02, Flags = RIDEV_REMOVE, Target = 0 };
        RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    /// <summary>True while more raw input is waiting in this thread's queue.</summary>
    public static bool IsMoreQueued => ((GetQueueStatus(QS_RAWINPUT) >> 16) & QS_RAWINPUT) != 0;

    /// <summary>Reads a WM_INPUT lParam. Absolute packets (pen tablets, remote desktop) are skipped.</summary>
    public static unsafe bool TryRead(nint lParam, out RawMousePacket packet)
    {
        packet = default;
        uint headerSize = (uint)sizeof(RAWINPUTHEADER);
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, null, ref size, headerSize);
        if (size == 0 || size > 256)
            return false;

        byte* buffer = stackalloc byte[(int)size];
        if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size)
            return false;

        if (((RAWINPUTHEADER*)buffer)->Type != RIM_TYPEMOUSE)
            return false;

        var mouse = (RAWMOUSE*)(buffer + headerSize);
        if ((mouse->Flags & MOUSE_MOVE_ABSOLUTE) != 0)
            return false;

        packet = new RawMousePacket(mouse->LastX, mouse->LastY, mouse->ButtonFlags);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public uint Flags; public nint Target; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER { public uint Type; public uint Size; public nint Device; public nint WParam; }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
        [FieldOffset(8)] public uint RawButtons;
        [FieldOffset(12)] public int LastX;
        [FieldOffset(16)] public int LastY;
        [FieldOffset(20)] public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern unsafe uint GetRawInputData(nint rawInput, uint command, void* data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern uint GetQueueStatus(uint flags);
}
