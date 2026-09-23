namespace BrainPlatform.Desktop.Infrastructure.Backend;

public sealed record BackendConnectionState(
    bool IsAvailable,
    string StatusText,
    string Detail,
    DateTimeOffset CheckedAt)
{
    public static BackendConnectionState Checking(DateTimeOffset checkedAt) => new(
        false,
        "正在检查",
        "正在连接本地科学引擎",
        checkedAt);

    public static BackendConnectionState Available(string service, DateTimeOffset checkedAt) => new(
        true,
        "已连接",
        string.IsNullOrWhiteSpace(service) ? "本地科学引擎可用" : service,
        checkedAt);

    public static BackendConnectionState Unavailable(string detail, DateTimeOffset checkedAt) => new(
        false,
        "未连接",
        detail,
        checkedAt);
}
