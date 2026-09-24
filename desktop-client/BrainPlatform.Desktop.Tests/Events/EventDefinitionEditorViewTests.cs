using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainPlatform.Desktop.Tests.Events;

public sealed class EventDefinitionEditorViewTests
{
    [Fact]
    public void Shortcut_scope_items_can_instantiate_their_data_triggers()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        "/BrainPlatform.Desktop;component/Shared/Styles/ControlStyles.xaml",
                        UriKind.RelativeOrAbsolute),
                });

                var view = new EventDefinitionEditorView();
                view.Measure(new Size(1_000, 800));
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

                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

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
