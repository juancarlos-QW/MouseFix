using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace MouseFix.Core;

/// <summary>Windows power plans (what powercfg does), behind an interface so the logic can be tested.</summary>
public interface IPowerPlans
{
    /// <summary>All power plans on this PC.</summary>
    IReadOnlyList<Guid> List();
    Guid GetActive();
    void SetActive(Guid plan);
    bool Exists(Guid plan);
    string GetName(Guid plan);
    /// <summary>Copies a plan; returns the new plan's id.</summary>
    Guid Duplicate(Guid source);
    void Rename(Guid plan, string name, string description);
    /// <summary>Sets a value for both plugged in (AC) and on battery (DC). Returns false if this PC doesn't have the setting.</summary>
    bool TrySetValue(Guid plan, Guid subgroup, Guid setting, uint value);
    /// <summary>The plugged-in value, or null if this PC doesn't have the setting.</summary>
    uint? ReadValue(Guid plan, Guid subgroup, Guid setting);
    void Delete(Guid plan);
}

/// <summary>Real implementation using powrprof.dll. Works without administrator rights.</summary>
public sealed partial class WindowsPowerPlans : IPowerPlans
{
    private const uint ErrorSuccess = 0;
    private const uint AccessScheme = 16;

    public IReadOnlyList<Guid> List()
    {
        var plans = new List<Guid>();
        var buffer = new byte[16];
        for (uint index = 0; ; index++)
        {
            uint size = (uint)buffer.Length;
            if (PowerEnumerate(0, 0, 0, AccessScheme, index, buffer, ref size) != ErrorSuccess)
                break; // ERROR_NO_MORE_ITEMS
            plans.Add(new Guid(buffer));
        }
        return plans;
    }

    public Guid GetActive()
    {
        Check(PowerGetActiveScheme(0, out nint ptr));
        try
        {
            return Marshal.PtrToStructure<Guid>(ptr);
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    public void SetActive(Guid plan) => Check(PowerSetActiveScheme(0, in plan));

    public bool Exists(Guid plan)
    {
        uint size = 0;
        return PowerReadFriendlyName(0, in plan, 0, 0, null, ref size) == ErrorSuccess && size > 0;
    }

    public string GetName(Guid plan)
    {
        uint size = 0;
        if (PowerReadFriendlyName(0, in plan, 0, 0, null, ref size) != ErrorSuccess || size == 0)
            return "?";
        var buffer = new byte[size];
        if (PowerReadFriendlyName(0, in plan, 0, 0, buffer, ref size) != ErrorSuccess)
            return "?";
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    public Guid Duplicate(Guid source)
    {
        nint destination = 0;
        Check(PowerDuplicateScheme(0, in source, ref destination));
        try
        {
            return Marshal.PtrToStructure<Guid>(destination);
        }
        finally
        {
            LocalFree(destination);
        }
    }

    public void Rename(Guid plan, string name, string description)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name + "\0");
        Check(PowerWriteFriendlyName(0, in plan, 0, 0, nameBytes, (uint)nameBytes.Length));
        var descriptionBytes = Encoding.Unicode.GetBytes(description + "\0");
        PowerWriteDescription(0, in plan, 0, 0, descriptionBytes, (uint)descriptionBytes.Length);
    }

    public bool TrySetValue(Guid plan, Guid subgroup, Guid setting, uint value) =>
        PowerWriteACValueIndex(0, in plan, in subgroup, in setting, value) == ErrorSuccess &
        PowerWriteDCValueIndex(0, in plan, in subgroup, in setting, value) == ErrorSuccess;

    public uint? ReadValue(Guid plan, Guid subgroup, Guid setting) =>
        PowerReadACValueIndex(0, in plan, in subgroup, in setting, out uint value) == ErrorSuccess ? value : null;

    public void Delete(Guid plan) => Check(PowerDeleteScheme(0, in plan));

    private static void Check(uint result)
    {
        if (result != ErrorSuccess)
            throw new Win32Exception((int)result);
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerEnumerate(nint root, nint schemeGuid, nint subgroupGuid, uint accessFlags, uint index, byte[] buffer, ref uint size);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(nint root, out nint activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(nint root, in Guid scheme);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerDuplicateScheme(nint root, in Guid source, ref nint destination);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerDeleteScheme(nint root, in Guid scheme);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadFriendlyName(nint root, in Guid scheme, nint subgroup, nint setting, byte[]? buffer, ref uint size);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteFriendlyName(nint root, in Guid scheme, nint subgroup, nint setting, byte[] buffer, uint size);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteDescription(nint root, in Guid scheme, nint subgroup, nint setting, byte[] buffer, uint size);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteACValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteDCValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadACValueIndex(nint root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
