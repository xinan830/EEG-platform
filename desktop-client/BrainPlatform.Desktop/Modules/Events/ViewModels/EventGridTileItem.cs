using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Events.Domain;
using BrainPlatform.Desktop.Modules.Events.Services;

namespace BrainPlatform.Desktop.Modules.Events.ViewModels;

/// <summary>
/// Presentation tile for one enabled event definition inside the shared
/// Acquisition / Review annotation grid popup.
/// </summary>
public sealed class EventGridTileItem : ObservableObject
{
    private bool isActiveInterval;
    private string? activePromptText;

    public EventGridTileItem(EventDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        IconImage = EventIconAssetService.TryLoadImage(definition.IconPath);
        ColorBrush = ResolveColorBrush(definition.Color);
        InitialGlyph = string.IsNullOrWhiteSpace(definition.Name) ? "E" : definition.Name.Trim()[..1];
        SubtitleText = definition.IsIntervalKind
            ? $"{definition.EffectiveStartLabel} → {definition.EffectiveEndLabel}"
            : string.IsNullOrWhiteSpace(definition.Shortcut)
                ? string.Empty
                : definition.Shortcut;
    }

    public EventDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Name => Definition.Name;

    public bool IsIntervalKind => Definition.IsIntervalKind;

    public ImageSource? IconImage { get; }

    public bool HasIcon => IconImage is not null;

    public Brush ColorBrush { get; }

    public string InitialGlyph { get; }

    public string SubtitleText { get; }

    public bool IsActiveInterval
    {
        get => isActiveInterval;
        set
        {
            if (SetProperty(ref isActiveInterval, value))
            {
                RaisePropertyChanged(nameof(DisplaySubtitle));
                RaisePropertyChanged(nameof(HasSubtitle));
            }
        }
    }

    public string? ActivePromptText
    {
        get => activePromptText;
        set
        {
            if (SetProperty(ref activePromptText, value))
            {
                RaisePropertyChanged(nameof(DisplaySubtitle));
                RaisePropertyChanged(nameof(HasSubtitle));
            }
        }
    }

    public string DisplaySubtitle => IsActiveInterval
        ? (ActivePromptText ?? $"点击结束：{Definition.EffectiveEndLabel}")
        : SubtitleText;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(DisplaySubtitle);

    private static Brush ResolveColorBrush(string? colorText)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(colorText))
            {
                var color = (Color)ColorConverter.ConvertFromString(colorText.Trim());
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // Fall back to brand blue when color string is malformed.
        }

        var fallback = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        fallback.Freeze();
        return fallback;
    }
}
