using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;

/// <summary>STFT-only structured and parsed preview state.</summary>
public sealed class StftPreviewState : ObservableObject
{
    private StftPreview? result;
    private StructuredPreviewResponse? structuredPreview;
    private DynamicWindowResponse? windowPreview;
    private string? projectionKey;

    public StftPreview? Result { get => result; set => SetProperty(ref result, value); }
    public StructuredPreviewResponse? StructuredPreview { get => structuredPreview; set => SetProperty(ref structuredPreview, value); }
    public DynamicWindowResponse? WindowPreview { get => windowPreview; set => SetProperty(ref windowPreview, value); }
    public string? ProjectionKey { get => projectionKey; private set => SetProperty(ref projectionKey, value); }

    public void AppendWindow(DynamicWindowResponse response)
    {
        if (response.Identity is not { } identity)
        {
            Clear();
            return;
        }
        var key = $"{response.SessionId}|{ContextKey(identity)}";
        var next = response.State == "ready" ? StftPreview.FromWindow(response) : null;
        if (ProjectionKey is null || ProjectionKey != key)
        {
            Clear();
            ProjectionKey = key;
            WindowPreview = response;
            Result = next;
            return;
        }

        if (next is null)
        {
            WindowPreview = response;
            return;
        }

        Result = Result is null ? next : StftPreview.Append(Result, next);
        WindowPreview = response;
    }

    public void Clear()
    {
        Result = null;
        StructuredPreview = null;
        WindowPreview = null;
        ProjectionKey = null;
    }

    private static string ContextKey(JsonElement identity)
    {
        if (identity.ValueKind != JsonValueKind.Object)
            return identity.GetRawText();
        var fields = identity.EnumerateObject()
            .Where(property => property.Name is not ("start_sample" or "end_sample" or "generation"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(property => property.Name, property => property.Value.GetRawText(), StringComparer.Ordinal);
        return JsonSerializer.Serialize(fields);
    }
}
