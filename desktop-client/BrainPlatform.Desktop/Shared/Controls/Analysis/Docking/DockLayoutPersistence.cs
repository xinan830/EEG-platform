using System.IO;

namespace BrainPlatform.Desktop.Shared.Controls.Analysis.Docking;

/// <summary>Persists custom dock tree layouts per workspace key to %LocalAppData%\BrainPlatform.</summary>
public static class DockLayoutPersistence
{
    public static string DefaultFilePath => GetFilePath("review");

    public static string GetFilePath(string? workspaceKey)
    {
        var normalized = string.IsNullOrWhiteSpace(workspaceKey)
            ? "review"
            : string.Concat(workspaceKey.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            $"{normalized}-dock-layout.json");
    }

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
