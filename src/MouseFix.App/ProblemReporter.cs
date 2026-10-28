using System.Diagnostics;
using MouseFix.Core;

namespace MouseFix.App;

/// <summary>Opens a pre-filled GitHub issue in the browser; the user reviews and sends it there.</summary>
internal static class ProblemReporter
{
    public static void Open(MouseFixService service, ProtectionMonitor monitor)
    {
        var current = service.Current;
        string profile = monitor.ActiveProfile is { } p ? L.T($" (perfil \"{p.DisplayName}\")", $" (profile \"{p.DisplayName}\")") : "";
        string mouseState = L.T($"aceleração {(current.IsAccelerationEnabled ? "ligada" : "desligada")}, ", $"acceleration {(current.IsAccelerationEnabled ? "on" : "off")}, ") +
                            L.T($"velocidade {current.Speed}/20, escolhida {service.TargetSpeed}/20{profile}", $"speed {current.Speed}/20, chosen {service.TargetSpeed}/20{profile}");

        var emulator = BlueStacksProfiles.Find(BlueStacksProfiles.DefaultRoot).FirstOrDefault();
        string? emulatorText = emulator is null
            ? null
            : L.T($"{emulator.EmulatorName} · {emulator.GameName} (aceleração do emulador {(emulator.AnyAcceleration ? "ligada" : "desligada")})", $"{emulator.EmulatorName} · {emulator.GameName} (emulator acceleration {(emulator.AnyAcceleration ? "on" : "off")})");

        var info = new IssueReportInfo(
            AppVersion: typeof(ProblemReporter).Assembly.GetName().Version?.ToString(3) ?? "?",
            WindowsVersion: WindowsVersion(),
            DisplayScalePercent: new WindowsMouseSettingsStore().GetDisplayScalePercent(),
            MouseState: mouseState,
            MouseSoftware: MouseSoftware.FindRunning(),
            Emulator: emulatorText,
            LogTail: IssueReport.ReadLogTail(Log.FilePath, 30, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        OpenInBrowser(IssueReport.BuildUrl(info));
    }

    public static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    public static void OpenLogFolder()
    {
        var folder = System.IO.Path.GetDirectoryName(Log.FilePath)!;
        System.IO.Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
    }

    // Windows 11 still reports major version 10; build 22000 and later is Windows 11.
    private static string WindowsVersion()
    {
        var v = Environment.OSVersion.Version;
        return $"{(v.Major == 10 && v.Build >= 22000 ? "Windows 11" : $"Windows {v.Major}")} (build {v.Build})";
    }
}
