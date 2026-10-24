using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseFix.Core;

/// <summary>Who caused a change to the mouse settings.</summary>
public enum ChangeSource
{
    /// <summary>The user, from MouseFix (buttons, slider, settings).</summary>
    User,
    /// <summary>A speed profile switched because a program opened or closed.</summary>
    Profile,
    /// <summary>Another program changed the mouse and MouseFix put the user's settings back.</summary>
    OtherProgram,
    /// <summary>Restoring the settings from before MouseFix.</summary>
    Restore,
}

/// <param name="Time">When it happened (the latest time, for grouped entries).</param>
/// <param name="Description">What changed, in plain words.</param>
/// <param name="Verified">True when MouseFix read the settings back and they matched.</param>
/// <param name="Problem">What did not take effect, when <paramref name="Verified"/> is false.</param>
/// <param name="Count">How many identical changes were grouped into this entry.</param>
public sealed record ChangeEntry(
    DateTime Time,
    ChangeSource Source,
    string Description,
    bool Verified,
    string? Problem = null,
    string? Program = null,
    int Count = 1);

/// <summary>
/// Recent changes to the mouse settings, newest first, kept in %AppData%\MouseFix\history.json so the user can see
/// what changed, when, who did it and whether it was verified.
/// </summary>
public sealed class ChangeHistory
{
    public const int MaxEntries = 200;

    // Repeats of the same change this close together become one entry ("3x") instead of flooding the list.
    private static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly List<ChangeEntry> _entries;

    public ChangeHistory(string path)
    {
        _path = path;
        _entries = Load(path);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseFix", "history.json");

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ChangeEntry> Entries => _entries;

    public event EventHandler? Changed;

    public void Add(ChangeEntry entry)
    {
        var last = _entries.FirstOrDefault();
        if (last is not null && last.Source == entry.Source && last.Description == entry.Description &&
            last.Program == entry.Program && last.Verified == entry.Verified && entry.Time - last.Time < GroupWindow)
        {
            _entries[0] = entry with { Count = last.Count + 1 };
        }
        else
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries, Json));
        }
        catch (IOException)
        {
            // History is a convenience; failing to save it must never break applying settings.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static List<ChangeEntry> Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<ChangeEntry>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }
}
