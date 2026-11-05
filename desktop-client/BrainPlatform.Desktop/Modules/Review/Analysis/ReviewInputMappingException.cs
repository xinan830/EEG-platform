namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public sealed class ReviewInputMappingException(
    string code,
    string message,
    string? role) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public string? Role { get; } = role;
}
