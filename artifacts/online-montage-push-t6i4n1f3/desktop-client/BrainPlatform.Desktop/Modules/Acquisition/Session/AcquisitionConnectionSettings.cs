using System.Text.Json;
using System.IO;

namespace BrainPlatform.Desktop.Modules.Acquisition.Session;

/// <summary>
/// Local workstation preferences only. This file never contains EEG samples,
/// scientific results, or device data.
/// </summary>
public sealed record AcquisitionConnectionSettings(
    string SdkLibraryPath,
    string ReferenceRangeText,
    string BipolarRangeText,
    string RecordingDirectory,
    int? SamplingRateHz = null,
    double LiveHighPassHz = 1,
    double LiveLowPassHz = 30,
    double LiveNotchHz = 0,
    double PaperSpeedMillimetersPerSecond = 30,
    ViewModels.HorizontalTimeScaleMode HorizontalTimeScaleMode = ViewModels.HorizontalTimeScaleMode.PaperSpeed,
    double TimebaseSecondsPerScreen = 10)
{
    public static AcquisitionConnectionSettings CreateDefault() => new(
        string.Empty,
        string.Empty,
        string.Empty,
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Brain Platform",
            "Recordings"),
        null,
        1,
        30,
        0,
        30,
        ViewModels.HorizontalTimeScaleMode.PaperSpeed,
        10);
}

public interface IAcquisitionSettingsStore
{
    AcquisitionConnectionSettings Load();

    Task SaveAsync(AcquisitionConnectionSettings settings, CancellationToken cancellationToken);
}

public sealed class LocalAcquisitionSettingsStore : IAcquisitionSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string settingsPath;

    public LocalAcquisitionSettingsStore(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrainPlatform",
            "acquisition-settings.json");
    }

    public AcquisitionConnectionSettings Load()
    {
        if (!File.Exists(settingsPath))
        {
            return AcquisitionConnectionSettings.CreateDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<AcquisitionConnectionSettings>(File.ReadAllText(settingsPath))
                ?? AcquisitionConnectionSettings.CreateDefault();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Local acquisition settings are not valid JSON.", exception);
        }
    }

    public async Task SaveAsync(AcquisitionConnectionSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("Acquisition settings path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
