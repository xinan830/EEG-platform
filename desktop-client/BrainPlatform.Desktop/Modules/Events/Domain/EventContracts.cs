namespace BrainPlatform.Desktop.Modules.Events.Domain;

public enum EventSource
{
    ManualButton,
    KeyboardShortcut,
    DeviceTrigger,
    ImportedAnnotation,
    Algorithm,
    System,
}

public enum EventDefinitionSource
{
    System,
    User,
}

public enum ShortcutScope
{
    Global,
    Acquisition,
    Review,
}

public enum EventCoordinateStatus
{
    Resolved,
    UnavailableGap,
    UnavailableDiscontinuity,
}

public sealed record RecordingEventCoordinate(
    long RecordingRelativeSample,
    long SourceSampleCounter,
    EventCoordinateStatus Status = EventCoordinateStatus.Resolved,
    string? UnavailableReason = null)
{
    public bool IsDisplayable => Status == EventCoordinateStatus.Resolved;
}

public sealed record EventDefinition(
    string Id,
    string Code,
    string Name,
    string Description,
    string Color,
    bool IsEnabled,
    string? Shortcut,
    ShortcutScope ShortcutScope,
    EventDefinitionSource Source,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public bool IsSystem => Source == EventDefinitionSource.System;
}

public sealed record EventDefinitionSnapshot(
    string Code,
    string Name,
    string Color,
    int Version);

public sealed record RecordingEvent(
    string Id,
    string RecordingId,
    string DefinitionId,
    EventDefinitionSnapshot DefinitionSnapshot,
    EventSource Source,
    string? SourceDetail,
    string? ExternalCode,
    long StartSample,
    long DurationSamples,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Note = null,
    long? SourceSampleCounter = null,
    EventCoordinateStatus CoordinateStatus = EventCoordinateStatus.Resolved,
    string? CoordinateUnavailableReason = null)
{
    public bool IsInterval => DurationSamples > 0;

    public bool IsDisplayable => CoordinateStatus == EventCoordinateStatus.Resolved;

    public long EndSampleExclusive => checked(StartSample + Math.Max(0, DurationSamples));
}

public sealed record RecordingEventQuery(
    string? RecordingId = null,
    long? StartSample = null,
    long? EndSampleExclusive = null,
    string? DefinitionId = null,
    string? DefinitionCode = null,
    EventSource? Source = null)
{
    public void Validate()
    {
        if (StartSample is < 0 || EndSampleExclusive is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(StartSample), "Sample range cannot be negative.");
        }

        if (StartSample is not null && EndSampleExclusive is not null && StartSample > EndSampleExclusive)
        {
            throw new ArgumentException("The event query start sample must not exceed its end sample.");
        }
    }
}

public sealed class EventValidationException : InvalidOperationException
{
    public EventValidationException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}

public sealed record ShortcutConflict(string Shortcut, ShortcutScope Scope, string ExistingCommand);

public sealed record ShortcutRegistrationResult(bool Succeeded, ShortcutConflict? Conflict = null)
{
    public static ShortcutRegistrationResult Success() => new(true);

    public static ShortcutRegistrationResult Failed(ShortcutConflict conflict) => new(false, conflict);
}

public sealed record EventStoreEnvelope<T>(int SchemaVersion, IReadOnlyList<T> Items);

internal static class EventValidation
{
    private static readonly HashSet<string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transparent", "Black", "White", "Red", "Green", "Blue", "Yellow", "Orange", "Purple", "Cyan", "Magenta",
    };

    public static void ValidateDefinition(EventDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new EventValidationException("id_required", "事件定义必须有唯一 Id。");
        if (string.IsNullOrWhiteSpace(definition.Code))
            throw new EventValidationException("code_required", "事件 Code 不能为空。");
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new EventValidationException("name_required", "事件名称不能为空。");
        if (definition.Description.Length > 200)
            throw new EventValidationException("description_too_long", "事件说明不能超过 200 个字符。");
        if (definition.Version <= 0)
            throw new EventValidationException("version_invalid", "事件定义版本必须为正数。");
        if (!IsValidColor(definition.Color))
            throw new EventValidationException("color_invalid", "事件颜色必须是十六进制颜色或已知颜色名。");
        if (definition.Source == EventDefinitionSource.System && !definition.IsEnabled && definition.Shortcut is not null)
            throw new EventValidationException("system_shortcut_invalid", "停用系统事件不能保留快捷键。");
    }

    public static void ValidateRecordingEvent(RecordingEvent item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.RecordingId) || string.IsNullOrWhiteSpace(item.DefinitionId))
            throw new EventValidationException("identity_required", "记录事件必须包含 Id、RecordingId 和 DefinitionId。");
        if (item.StartSample < 0)
            throw new EventValidationException("start_sample_invalid", "事件起始采样点不能为负数。");
        if (item.DurationSamples < 0)
            throw new EventValidationException("duration_invalid", "事件持续采样数不能为负数。");
        if (item.SourceSampleCounter is < 0)
            throw new EventValidationException("source_sample_counter_invalid", "原始设备采样计数不能为负数。");
        if (item.CoordinateStatus == EventCoordinateStatus.Resolved && item.CoordinateUnavailableReason is not null)
            throw new EventValidationException("coordinate_status_invalid", "可用事件不能包含不可用坐标原因。");
        if (item.CoordinateStatus != EventCoordinateStatus.Resolved && string.IsNullOrWhiteSpace(item.CoordinateUnavailableReason))
            throw new EventValidationException("coordinate_status_invalid", "不可用事件必须包含结构化坐标原因。");
        if (string.IsNullOrWhiteSpace(item.DefinitionSnapshot.Code) || string.IsNullOrWhiteSpace(item.DefinitionSnapshot.Name))
            throw new EventValidationException("snapshot_invalid", "事件历史快照必须包含 Code 和名称。");
    }

    private static bool IsValidColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        if (NamedColors.Contains(color.Trim())) return true;
        var value = color.Trim();
        if (value[0] != '#') return false;
        return value.Length is 7 or 9 && value.Skip(1).All(Uri.IsHexDigit);
    }
}
