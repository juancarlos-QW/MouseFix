using System.Diagnostics;
using System.IO;
using MouseFix.Core;

namespace MouseFix.App;

/// <summary>
/// Keeps an old MouseFix from winning over this one: an older copy left running (or set to start with Windows,
/// e.g. a 1.0.0 in Downloads) would otherwise keep applying its old settings after the user installed a new version.
/// </summary>
internal static class OlderCopies
{
    public static Version CurrentVersion => typeof(OlderCopies).Assembly.GetName().Version is { } v
        ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0))
        : new Version(0, 0, 0);

    /// <summary>The version of a MouseFix exe on disk (major.minor.patch), or null if it doesn't exist or can't be read.</summary>
    public static Version? VersionOf(string exe)
    {
        try
        {
            if (!File.Exists(exe))
                return null;
            var info = FileVersionInfo.GetVersionInfo(exe);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // A copy being developed or tested must not take over the user's own "start with Windows".
    private static bool IsDevelopmentBuild(string exe) =>
        exe.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) || exe.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Points "start with Windows" at this copy when it opens an older or missing one. Returns the old exe, if changed.</summary>
    public static string? PointAutoStartHere()
    {
        string? current = Environment.ProcessPath;
        string? registered = AutoStart.RegisteredExe;
        if (current is null || IsDevelopmentBuild(current) || !AutoStart.ShouldPointHere(registered, current, CurrentVersion, VersionOf))
            return null;
        AutoStart.Enable(current);
        return registered;
    }

    /// <summary>
    /// Another MouseFix holds the single-instance lock. If it is an older version, closes it and takes the lock.
    /// Returns the version that was closed, or null when the running copy is the same or newer (or couldn't be closed).
    /// </summary>
    public static Version? TakeOver(Mutex instanceMutex, string exitEventName)
    {
        var older = new List<(Process Process, Version Version)>();
        foreach (var p in Process.GetProcessesByName("MouseFix"))
        {
            try
            {
                if (p.Id != Environment.ProcessId && p.MainModule?.FileName is string exe && VersionOf(exe) is Version v && v < CurrentVersion)
                {
                    older.Add((p, v));
                    continue;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Can't read it (e.g. it runs as administrator, or just closed): leave it alone.
            }
            p.Dispose();
        }
        if (older.Count == 0)
            return null;

        try
        {
            // Since 1.1.0 MouseFix closes cleanly when asked; 1.0.x has no way to be asked, so it is ended.
            if (EventWaitHandle.TryOpenExisting(exitEventName, out var exitEvent))
            {
                using (exitEvent)
                    exitEvent.Set();
                if (Acquire(instanceMutex, TimeSpan.FromSeconds(8)))
                    return older[0].Version;
            }
            foreach (var (process, _) in older)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Already gone.
                }
            }
            return Acquire(instanceMutex, TimeSpan.FromSeconds(5)) ? older[0].Version : null;
        }
        finally
        {
            foreach (var (process, _) in older)
                process.Dispose();
        }
    }

    private static bool Acquire(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true; // the old copy ended without releasing it; it is ours now
        }
    }
}
