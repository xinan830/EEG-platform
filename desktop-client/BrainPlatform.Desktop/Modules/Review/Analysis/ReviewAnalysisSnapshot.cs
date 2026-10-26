using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Immutable algorithm-owned scientific request captured for review.</summary>
public sealed record ReviewAnalysisSnapshot(
    string RecordingId,
    string SourceSha256,
    string AlgorithmId,
    long CursorSample,
    JsonElement Configuration,
    string? IdempotencyKey,
    string? DefinitionId = null,
    string? DefinitionVersion = null,
    string? ImplementationIdentity = null,
    ReviewScientificConfigurationSnapshot? ScientificConfiguration = null)
{
    public static ReviewAnalysisSnapshot From(AnalysisRunRequest request, RegisteredRecording recording,
        long cursorSample, AlgorithmCatalogItem? algorithm = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(recording);
        if (string.IsNullOrWhiteSpace(request.RecordingId))
            throw new ArgumentException("分析快照必须包含记录 ID。", nameof(request));
        if (!string.Equals(request.RecordingId, recording.Id, StringComparison.Ordinal))
            throw new ArgumentException("算法请求与注册记录身份不匹配。", nameof(request));
        if (string.IsNullOrWhiteSpace(recording.SourceSha256))
            throw new ArgumentException("注册记录缺少源文件 checksum，不能建立可追溯分析快照。", nameof(recording));
        if (cursorSample < 0) throw new ArgumentOutOfRangeException(nameof(cursorSample));
        if (!string.Equals(request.AnalysisType, "official_algorithm", StringComparison.Ordinal))
            throw new ArgumentException("回溯算法快照必须是官方算法请求。", nameof(request));
        if (request.Config.ValueKind != JsonValueKind.Object ||
            !request.Config.TryGetProperty("algorithm_id", out var algorithmValue) ||
            algorithmValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(algorithmValue.GetString()))
        {
            throw new ArgumentException("回溯算法快照缺少 algorithm_id。", nameof(request));
        }

        if (algorithm is not null && !string.Equals(algorithm.Id, algorithmValue.GetString(), StringComparison.Ordinal))
            throw new ArgumentException("算法目录项与请求 algorithm_id 不匹配。", nameof(algorithm));

        return new ReviewAnalysisSnapshot(request.RecordingId, recording.SourceSha256, algorithmValue.GetString()!,
            cursorSample, request.Config.Clone(), request.IdempotencyKey,
            algorithm?.DefinitionId, algorithm?.DefinitionVersion, algorithm?.ImplementationIdentity);
    }

    public AnalysisRunRequest ToRunRequest() =>
        new(RecordingId, "official_algorithm", Configuration.Clone(), IdempotencyKey);

    public ReviewAnalysisSnapshot BindScientificConfiguration(string canonicalJson, string sha256)
    {
        var resolved = ReviewScientificConfigurationSnapshot.Parse(canonicalJson, sha256);
        if (!resolved.ContainsRequestedConfiguration(Configuration))
            throw new InvalidOperationException("后端科学配置与不可变分析快照不一致，已拒绝绑定。");
        return this with { ScientificConfiguration = resolved };
    }
}

/// <summary>Backend-resolved scientific inputs bound to a review request snapshot.</summary>
public sealed record ReviewScientificConfigurationSnapshot(
    string Contract,
    JsonElement NormalizedConfiguration,
    JsonElement ScientificContext,
    string CanonicalJson,
    string Sha256)
{
    public static ReviewScientificConfigurationSnapshot Parse(string canonicalJson, string sha256)
    {
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
        if (!string.Equals(actual, sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("按需响应科学配置摘要校验失败，已拒绝绑定。");
        using var document = JsonDocument.Parse(canonicalJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("scientific_configuration_contract", out var contract) ||
            contract.ValueKind != JsonValueKind.String || contract.GetString() != "scientific-config-v1" ||
            !root.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("context", out var context) || context.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("按需响应科学配置快照格式无效，已拒绝绑定。");
        return new ReviewScientificConfigurationSnapshot(contract.GetString()!, config.Clone(),
            context.Clone(), canonicalJson, sha256);
    }

    public bool ContainsRequestedConfiguration(JsonElement requested)
    {
        foreach (var property in requested.EnumerateObject())
        {
            if (!NormalizedConfiguration.TryGetProperty(property.Name, out var value) ||
                !ReviewJsonValueComparer.Equivalent(value, property.Value)) return false;
        }
        return true;
    }
}

internal static class ReviewJsonValueComparer
{
    public static bool Equivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        if (left.ValueKind == JsonValueKind.Object)
        {
            var a = left.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
            var b = right.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
            return a.Length == b.Length && a.Zip(b).All(pair => pair.First.Name == pair.Second.Name &&
                Equivalent(pair.First.Value, pair.Second.Value));
        }
        if (left.ValueKind == JsonValueKind.Array)
            return left.GetArrayLength() == right.GetArrayLength() &&
                left.EnumerateArray().Zip(right.EnumerateArray()).All(pair => Equivalent(pair.First, pair.Second));
        if (left.ValueKind == JsonValueKind.Number)
            return left.TryGetDecimal(out var a) && right.TryGetDecimal(out var b) ? a == b : left.GetRawText() == right.GetRawText();
        return left.ValueKind switch
        {
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false,
        };
    }
}
