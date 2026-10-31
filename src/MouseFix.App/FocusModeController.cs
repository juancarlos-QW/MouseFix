using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MouseFix.Core;

namespace MouseFix.App;

/// <summary>
/// Does what <see cref="FocusMode"/> describes: slows the other programs down and closes the apps the user picked
/// while the emulator runs, and puts everything back afterwards. What it changed is saved in the settings, so a
/// crash or restart still puts it back.
/// </summary>
public sealed class FocusModeController
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetInformation = 0x0200;
    private const uint ProcessSetQuota = 0x0100;
    private const uint NormalPriorityClass = 0x20;
    private const uint BelowNormalPriorityClass = 0x4000;
    private const int ProcessPowerThrottling = 4;
    private const uint ThrottleExecutionSpeed = 1;

    private readonly AppSettings _settings;
    private readonly Action _save;
    // Processes already looked at in this focus session, so new ones can be caught without re-checking all.
    private readonly HashSet<int> _seen = [];
    private readonly string _windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private readonly int _session = Process.GetCurrentProcess().SessionId;

    public FocusModeController(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    public bool IsActive { get; private set; }

    /// <summary>Something from an earlier run is still slowed down or closed (MouseFix closed or crashed mid-game).</summary>
    public bool HasLeftovers => _settings.FocusThrottled.Count > 0 || _settings.FocusClosed.Count > 0;

    /// <summary>Turns focus mode on. Returns what was done, for the history.</summary>
    public string Start()
    {
        IsActive = true;
        _seen.Clear();
        var closed = CloseChosenApps();
        // Measured as the free memory Windows reports, so memory shared between programs isn't counted twice.
        long freeBefore = AvailableMemory();
        int count = ThrottleNew();
        long freed = Math.Max(0, AvailableMemory() - freeBefore);
        string description = L.T($"Modo foco ligado: {FocusMode.Programs(count)} com menos prioridade, {FocusMode.FormatBytes(freed)} de memória liberada",
            $"Focus mode on: {FocusMode.Programs(count)} at lower priority, {FocusMode.FormatBytes(freed)} of memory freed");
        if (closed.Count > 0)
            description += L.T($"; fechados: {string.Join(", ", closed)}", $"; closed: {string.Join(", ", closed)}");
        return description;
    }

    /// <summary>Slows down programs opened since focus mode started. Returns how many.</summary>
    public int CatchNew() => IsActive ? ThrottleNew() : 0;

    /// <summary>Puts everything back. Returns what was done, for the history.</summary>
    public string Stop()
    {
        IsActive = false;
        _seen.Clear();
        int restored = 0;
        foreach (var entry in _settings.FocusThrottled)
        {
            var parts = entry.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int pid) && long.TryParse(parts[1], out long started) && Restore(pid, started))
                restored++;
        }
        var reopened = new List<string>();
        foreach (var entry in _settings.FocusClosed)
        {
            var parts = entry.Split('|', 2);
            var app = FocusMode.ClosableApps.FirstOrDefault(a => a.Key == parts[0]);
            if (app is not null && parts.Length == 2 && Reopen(app, parts[1]))
                reopened.Add(app.DisplayName);
        }
        _settings.FocusThrottled.Clear();
        _settings.FocusClosed.Clear();
        _save();

        string description = L.T($"Modo foco desligado: {FocusMode.Programs(restored)} de volta ao normal", $"Focus mode off: {FocusMode.Programs(restored)} back to normal");
        if (reopened.Count > 0)
            description += L.T($"; reabertos: {string.Join(", ", reopened)}", $"; reopened: {string.Join(", ", reopened)}");
        return description;
    }

    private List<string> CloseChosenApps()
    {
        var closed = new List<string>();
        foreach (var app in FocusMode.ClosableApps.Where(a => _settings.FocusCloseApps.Contains(a.Key)))
        {
            string? exe = null;
            foreach (var p in Process.GetProcessesByName(app.ProcessName))
            {
                using (p)
                {
                    try
                    {
                        if (p.SessionId != _session)
                            continue;
                        exe ??= ExePath(p.Id);
                        if (app.CloseArguments is null)
                            p.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                    {
                        // Runs as administrator or already closed: leave it.
                    }
                }
            }
            if (exe is null)
                continue;
            try
            {
                // Apps that can close themselves are asked to, so they save their state first.
                if (app.CloseArguments is not null)
                    Process.Start(new ProcessStartInfo(exe, app.CloseArguments) { UseShellExecute = false })?.Dispose();
                _settings.FocusClosed.Add($"{app.Key}|{exe}");
                closed.Add(app.DisplayName);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Log.Error($"Modo foco: não foi possível fechar {app.DisplayName}", ex);
            }
        }
        _save();
        return closed;
    }

    private bool Reopen(ClosableApp app, string exe)
    {
        var running = Process.GetProcessesByName(app.ProcessName);
        foreach (var p in running)
            p.Dispose();
        // Already back (the user opened it during the game): nothing to do.
        if (!File.Exists(exe) || running.Length > 0)
            return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe, app.ReopenArguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Modo foco: não foi possível reabrir {app.DisplayName}", ex);
            return false;
        }
    }

    private int ThrottleNew()
    {
        int count = 0;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (!_seen.Add(p.Id) || p.Id == Environment.ProcessId)
                    continue;
                try
                {
                    if (p.SessionId != _session)
                        continue;
                }
                catch (InvalidOperationException)
                {
                    continue; // already closed
                }
                if (Throttle(p.Id, p.ProcessName))
                    count++;
            }
        }
        if (count > 0)
            _save();
        return count;
    }

    /// <summary>Lowers one program if allowed: below-normal priority, efficiency mode, idle memory given back.</summary>
    private bool Throttle(int pid, string name)
    {
        nint handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetInformation | ProcessSetQuota, false, pid);
        if (handle == 0)
            return false; // runs as administrator or is a system process
        try
        {
            // Only programs at normal priority: one the user (or the program itself) set otherwise is left as is.
            if (!FocusMode.MayThrottle(name, ExePath(handle), _windowsFolder) || GetPriorityClass(handle) != NormalPriorityClass
                || !GetProcessTimes(handle, out long started, out _, out _, out _))
                return false;
            if (!SetPriorityClass(handle, BelowNormalPriorityClass))
                return false;
            _settings.FocusThrottled.Add($"{pid}:{started}");
            SetEfficiencyMode(handle, on: true);
            K32EmptyWorkingSet(handle);
            return true;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private bool Restore(int pid, long started)
    {
        nint handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetInformation, false, pid);
        if (handle == 0)
            return false; // closed meanwhile
        try
        {
            // Same start time, so it is the same program and not a new one that reused the id.
            if (!GetProcessTimes(handle, out long now, out _, out _, out _) || now != started)
                return false;
            SetEfficiencyMode(handle, on: false);
            return GetPriorityClass(handle) == BelowNormalPriorityClass && SetPriorityClass(handle, NormalPriorityClass);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // Windows 11's "efficiency mode" (Task Manager); off hands the decision back to Windows.
    private static void SetEfficiencyMode(nint handle, bool on)
    {
        var state = new PowerThrottlingState
        {
            Version = 1,
            ControlMask = on ? ThrottleExecutionSpeed : 0,
            StateMask = on ? ThrottleExecutionSpeed : 0,
        };
        SetProcessInformation(handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
    }

    private static long AvailableMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailablePhysical : 0;
    }

    private static string? ExePath(int pid)
    {
        nint handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
            return null;
        try
        {
            return ExePath(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string ExePath(nint handle)
    {
        var buffer = new StringBuilder(1024);
        uint size = (uint)buffer.Capacity;
        return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(nint handle, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern uint GetPriorityClass(nint handle);

    [DllImport("kernel32.dll")]
    private static extern bool SetPriorityClass(nint handle, uint priorityClass);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(nint handle, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessInformation(nint handle, int infoClass, ref PowerThrottlingState info, uint size);

    [DllImport("kernel32.dll")]
    private static extern bool K32EmptyWorkingSet(nint handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
