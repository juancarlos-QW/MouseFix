using System.Text.Json;

namespace MouseFix.Core;

/// <summary>Persists the user's original settings as JSON so they can always be restored.</summary>
public sealed class BackupStore
{
    private readonly string _path;

    public BackupStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseFix", "backup.json");

    public bool Exists => File.Exists(_path);

    public MouseSettings? Load() =>
        Exists ? JsonSerializer.Deserialize<MouseSettings>(File.ReadAllText(_path)) : null;

    public void Save(MouseSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Delete()
    {
        if (Exists)
            File.Delete(_path);
    }
}
