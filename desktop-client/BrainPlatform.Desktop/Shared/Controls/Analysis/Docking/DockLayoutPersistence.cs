using System.IO;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Persists custom Review dock tree layouts to %LocalAppData%\BrainPlatform.</summary>
public static class DockLayoutPersistence
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrainPlatform",
        "review-dock-layout.json");

    public static DockLayoutSnapshot? Load(string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return DockTreeOperations.DeserializeSnapshot(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(DockLayoutSnapshot snapshot, string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, DockTreeOperations.SerializeSnapshot(snapshot));
        File.Move(tempPath, path, overwrite: true);
    }

    public static void Clear(string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignore file lock / cleanup failures during reset.
        }
    }
}
