using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Shared.Controls;

/// <summary>Presentation-only labeled container; its content retains the page's bindings.</summary>
public sealed class EegParameterField : HeaderedContentControl
{
    static EegParameterField() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(EegParameterField), new FrameworkPropertyMetadata(typeof(EegParameterField)));
}
