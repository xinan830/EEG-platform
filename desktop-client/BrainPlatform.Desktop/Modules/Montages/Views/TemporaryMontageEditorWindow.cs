using System.Windows;

namespace BrainPlatform.Desktop.Modules.Montages.Views;

public sealed record TemporaryMontageEditorContext(MontageConfigurationWorkspaceViewModel MontageConfigurations);

/// <summary>Hosts the full montage settings with return-only session saving.</summary>
public sealed class TemporaryMontageEditorWindow : Window
{
    public MontageProfile? SavedProfile { get; private set; }

    public TemporaryMontageEditorWindow(ChannelConfigurationProfile source, MontageProfile? existing, Window? owner)
    {
        Title = "导联设置";
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Width = Math.Min(1280, SystemParameters.WorkArea.Width);
        Height = Math.Min(900, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(1000, Width);
        MinHeight = Math.Min(600, Height);
        var editor = new MontageConfigurationWorkspaceViewModel(
            new ChannelConfigurationWorkspaceViewModel(new ChannelMappingViewModel()));
        editor.BeginSessionProfile(source, existing);
        var view = new MontageDetailView { DataContext = new TemporaryMontageEditorContext(editor) };
        Content = view;
        editor.SessionProfileSaved += (_, profile) => { SavedProfile = profile; DialogResult = true; };
        view.SessionCloseRequested += (_, _) => DialogResult = false;
        Closed += (_, _) => editor.CloseDraft();
    }
}
