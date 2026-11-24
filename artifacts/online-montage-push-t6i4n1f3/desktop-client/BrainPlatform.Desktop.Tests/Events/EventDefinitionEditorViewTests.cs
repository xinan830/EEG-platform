using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Shared.Controls;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Events;

[Collection("WPF application resources")]
public sealed class EventDefinitionEditorViewTests(WpfApplicationFixture application)
{
    [Fact]
    public void Shortcut_scope_items_can_instantiate_their_data_triggers() => application.Run(() =>
    {
        var view = new EventDefinitionEditorView();
        view.Measure(new Size(1_000, 800));
        Assert.NotNull(FindVisualChild<ColorPicker>(view));
        var codeInput = Assert.IsType<TextBox>(view.FindName("EventCodeTextBox"));
        Assert.True(codeInput.IsReadOnly);
        var scopeSelector = FindVisualChild<ComboBox>(view);
        Assert.NotNull(scopeSelector);
        Assert.NotNull(scopeSelector.ItemTemplate);

        foreach (var scope in new[] { ShortcutScope.Review, ShortcutScope.Global })
        {
            var item = Assert.IsType<TextBlock>(scopeSelector.ItemTemplate.LoadContent());
            item.DataContext = scope;
            item.Measure(new Size(200, 40));
        }
    });

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } descendant) return descendant;
        }

        return null;
    }
}
