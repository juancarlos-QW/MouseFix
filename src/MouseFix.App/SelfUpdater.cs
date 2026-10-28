using System.Diagnostics;
using System.IO;
using MouseFix.Core;

namespace MouseFix.App;

/// <summary>
/// Installs a new release from inside the app. Installed copies run the new installer silently (it reopens MouseFix);
/// the portable exe swaps itself for the new one and restarts. Files are only used after their SHA-256 matches.
/// </summary>
internal static class SelfUpdater
{
    public const string AfterUpdateArgument = "--after-update";
    private const string RestartArgument = "--restart";
    private const string OldExeName = "MouseFix.old.exe";

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException(L.T("Caminho do MouseFix desconhecido.", "MouseFix's path is unknown."));

    /// <summary>Installed with the installer (it leaves its uninstaller next to the exe), as opposed to the portable exe.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(Path.GetDirectoryName(ExePath)!, "unins000.exe"));

    /// <summary>
    /// Downloads and verifies the update, then starts whatever takes over (the installer, or the new portable exe).
    /// When this returns, the caller must exit so the new version can replace or start.
    /// </summary>
    public static async Task StartUpdateAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        string folder = Path.Combine(Path.GetTempPath(), "MouseFix-update");
        if (IsInstalled)
        {
            var (file, checksum) = update.Installer ?? throw new InvalidOperationException(L.T("Esta versão não tem instalador para baixar.", "This version has no installer to download."));
            string installer = Path.Combine(folder, $"MouseFix-Setup-{update.Version.ToString(3)}.exe");
            await UpdateDownloader.DownloadVerifiedAsync(file, checksum, installer, progress, cancellationToken);
            // /SILENT shows only a progress bar; the installer closes this copy if needed and reopens MouseFix at the end.
            Process.Start(new ProcessStartInfo(installer, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true })?.Dispose();
            Log.Info($"Atualização para {update.Version.ToString(3)}: instalador verificado e iniciado.");
        }
        else
        {
            var (file, checksum) = update.Portable ?? throw new InvalidOperationException(L.T("Esta versão não tem o MouseFix.exe portátil para baixar.", "This version has no portable MouseFix.exe to download."));
            string exe = ExePath;
            string downloaded = exe + ".new";
            await UpdateDownloader.DownloadVerifiedAsync(file, checksum, downloaded, progress, cancellationToken);

            // A running exe can't be overwritten, but it can be renamed: move it aside and put the new one in its place.
            string old = Path.Combine(Path.GetDirectoryName(exe)!, OldExeName);
            if (File.Exists(old))
                File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(downloaded, exe);
            }
            catch
            {
                File.Move(old, exe); // put the current version back
                throw;
            }

            // The new copy waits for this one to exit before starting (only one MouseFix can run at a time).
            Process.Start(new ProcessStartInfo(exe, $"{AfterUpdateArgument} {Environment.ProcessId}") { UseShellExecute = false })?.Dispose();
            Log.Info($"Atualização para {update.Version.ToString(3)}: MouseFix.exe verificado e trocado.");
        }
    }

    /// <summary>Starts a new copy that waits for this one to exit (e.g. to show another language). The caller must exit.</summary>
    public static void Restart() =>
        Process.Start(new ProcessStartInfo(ExePath, $"{RestartArgument} {Environment.ProcessId}") { UseShellExecute = false })?.Dispose();

    /// <summary>
    /// Called at start: after an update or a restart, waits for the old copy to close (it holds the single-instance
    /// lock) and removes the file an update left behind.
    /// </summary>
    public static void FinishUpdate(string[] args)
    {
        int index = Array.IndexOf(args, AfterUpdateArgument);
        if (index < 0)
            index = Array.IndexOf(args, RestartArgument);
        if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int oldPid))
        {
            try
            {
                using var old = Process.GetProcessById(oldPid);
                old.WaitForExit(15000);
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
        }

        try
        {
            string old = Path.Combine(Path.GetDirectoryName(ExePath)!, OldExeName);
            if (File.Exists(old))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still locked; it is removed on a later start.
        }
    }

    public static bool JustUpdated(string[] args) => args.Contains(AfterUpdateArgument) || args.Contains("--updated");
}
