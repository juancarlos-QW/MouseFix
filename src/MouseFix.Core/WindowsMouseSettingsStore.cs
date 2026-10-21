using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MouseFix.Core;

/// <summary>
/// Real implementation backed by SystemParametersInfo and HKCU\Control Panel\Mouse.
/// Everything lives under the current user, so no admin rights are needed.
/// </summary>
public sealed partial class WindowsMouseSettingsStore : IMouseSettingsStore
{
    private const string MouseKeyPath = @"Control Panel\Mouse";

    private const uint SPI_GETMOUSE = 0x0003;
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPI_GETMOUSESPEED = 0x0070;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPI_GETWHEELSCROLLLINES = 0x0068;
    private const uint SPI_SETWHEELSCROLLLINES = 0x0069;
    private const nint HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;

    public MouseSettings Read()
    {
        var mouse = new int[3];
        if (!SystemParametersInfo(SPI_GETMOUSE, 0, mouse, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (!SystemParametersInfo(SPI_GETMOUSESPEED, 0, out int speed, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (!SystemParametersInfo(SPI_GETWHEELSCROLLLINES, 0, out int scrollLines, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        using var key = Registry.CurrentUser.OpenSubKey(MouseKeyPath);
        var x = key?.GetValue("SmoothMouseXCurve") as byte[] ?? SmoothMouseCurve.WindowsDefaultX;
        var y = key?.GetValue("SmoothMouseYCurve") as byte[] ?? SmoothMouseCurve.WindowsDefaultY;

        return new MouseSettings(mouse[2], mouse[0], mouse[1], speed, x, y, scrollLines);
    }

    public void Write(MouseSettings settings)
    {
        // Curves are read by Windows at sign-in, so write them first; the SPI calls below apply instantly.
        using (var key = Registry.CurrentUser.CreateSubKey(MouseKeyPath))
        {
            key.SetValue("SmoothMouseXCurve", settings.SmoothMouseXCurve, RegistryValueKind.Binary);
            key.SetValue("SmoothMouseYCurve", settings.SmoothMouseYCurve, RegistryValueKind.Binary);
        }

        // No SPIF_SENDCHANGE: it broadcasts synchronously and blocks this thread while any window
        // in the system is hung. The change applies to the cursor immediately either way.
        var mouse = new[] { settings.Threshold1, settings.Threshold2, settings.Acceleration };
        if (!SystemParametersInfo(SPI_SETMOUSE, 0, mouse, SPIF_UPDATEINIFILE))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (!SystemParametersInfo(SPI_SETMOUSESPEED, 0, settings.Speed, SPIF_UPDATEINIFILE))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // Unknown (e.g. a backup made before MouseFix managed scrolling): leave the current value alone.
        if (settings.ScrollLines is int lines)
        {
            if (!SystemParametersInfo(SPI_SETWHEELSCROLLLINES, (uint)lines, 0, SPIF_UPDATEINIFILE))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            SendNotifyMessage(HWND_BROADCAST, WM_SETTINGCHANGE, (nint)SPI_SETWHEELSCROLLLINES, 0);
        }

        // Let other programs (e.g. the Settings app) refresh, without waiting for them.
        SendNotifyMessage(HWND_BROADCAST, WM_SETTINGCHANGE, (nint)SPI_SETMOUSE, 0);
        SendNotifyMessage(HWND_BROADCAST, WM_SETTINGCHANGE, (nint)SPI_SETMOUSESPEED, 0);
    }

    public int GetDisplayScalePercent() => (int)Math.Round(GetDpiForSystem() * 100 / 96.0);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, [In, Out] int[] value, uint winIni);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    // SPI_SETMOUSESPEED takes the value itself in pvParam, not a pointer to it.
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, nint value, uint winIni);

    [LibraryImport("user32.dll", EntryPoint = "SendNotifyMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SendNotifyMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}

