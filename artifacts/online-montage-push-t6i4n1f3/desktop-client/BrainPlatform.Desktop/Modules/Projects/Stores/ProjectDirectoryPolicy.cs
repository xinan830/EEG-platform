using System.IO;

namespace BrainPlatform.Desktop.Modules.Projects.Stores;

/// <summary>Resolves executable-relative project storage and creates project folders without reusing a default destination.</summary>
public sealed class ProjectDirectoryPolicy(string? applicationDirectory = null)
{
    public string DefaultRoot { get; } = Path.GetFullPath(Path.Combine(applicationDirectory ?? AppContext.BaseDirectory, "data"));

    public string GetDefaultDirectory(string projectName)
    {
        var name = projectName.Trim();
        if (name.Length == 0) throw new InvalidOperationException("请输入项目名称。");
        if (name.Length > 255 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.'))
            throw new InvalidOperationException("项目名称不能作为文件夹名称，请去掉路径分隔符、特殊字符或末尾的句点，或选择自定义目录。");

        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        var isDeviceName = stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
             "123456789¹²³".Contains(stem[3]));
        if (isDeviceName)
            throw new InvalidOperationException("项目名称是 Windows 保留的文件夹名称，请更换名称或选择自定义目录。");

        return Path.Combine(DefaultRoot, name);
    }

    public void CreateProjectDirectory(string directory, bool isDefault)
    {
        if (isDefault && (Directory.Exists(directory) || File.Exists(directory)))
            throw new InvalidOperationException($"默认项目目录已存在：{directory}。请更换项目名称或选择其他目录。");

        if (!isDefault)
        {
            Create(directory, directory);
            return;
        }

        var stagingDirectory = Path.Combine(DefaultRoot, $".project-{Guid.NewGuid():N}.tmp");
        try
        {
            Create(stagingDirectory, directory);
            Directory.Move(stagingDirectory, directory);
        }
        catch (IOException) when (Directory.Exists(directory) || File.Exists(directory))
        {
            throw new InvalidOperationException($"默认项目目录已存在：{directory}。请更换项目名称或选择其他目录。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"无法创建项目目录“{directory}”。请检查写入权限和磁盘空间，或选择其他目录。", exception);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static void Create(string directory, string errorDirectory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "recordings"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"无法创建项目目录“{errorDirectory}”。请检查写入权限和磁盘空间，或选择其他目录。", exception);
        }
    }
}
