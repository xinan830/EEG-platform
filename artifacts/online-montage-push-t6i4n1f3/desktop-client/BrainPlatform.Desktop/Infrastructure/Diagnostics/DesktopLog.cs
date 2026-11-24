using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BrainPlatform.Desktop.Infrastructure.Diagnostics;

/// <summary>Bounded local diagnostics; never receives request payloads or EEG values.</summary>
public static class DesktopLog
{
    private const long MaxBytes = 10L * 1024L * 1024L;
    private const int BackupCount = 5;
    private static readonly object Gate = new();

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrainPlatform", "logs");

    public static string PerformanceScenario
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("BRAIN_REVIEW_PERF_SCENARIO");
            return !string.IsNullOrWhiteSpace(value) &&
                   value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
                ? value
                : "unspecified";
        }
    }

    public static bool PerformanceDiagnosticsEnabled => string.Equals(
        Environment.GetEnvironmentVariable("BRAIN_REVIEW_PERF_DIAGNOSTICS"), "1", StringComparison.Ordinal);

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warning(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} {level} {Sanitize(message)}";
            if (exception is not null)
            {
                line += $" exception_type={exception.GetType().FullName} exception={Sanitize(exception.ToString())}";
            }

            lock (Gate)
            {
                var directory = LogDirectory;
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"desktop-{DateTimeOffset.Now:yyyyMMdd}.log");
                RotateIfNeeded(path);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never change acquisition, review, or analysis behavior.
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxBytes)
        {
            return;
        }

        for (var index = BackupCount - 1; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            var target = $"{path}.{index + 1}";
            if (File.Exists(source)) File.Move(source, target, overwrite: true);
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }

    private static string Sanitize(string value)
    {
        var withoutPaths = Regex.Replace(value, @"(?i)\b[A-Z]:\\[^\r\n]*", "<path>");
        return withoutPaths.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
