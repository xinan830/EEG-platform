using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Projects.Views;

public sealed partial class RenameRecordingDialog : Window
{
    public RenameRecordingDialog(ProjectWorkspaceViewModel workspace, ProjectRecordingRow recording)
    {
        InitializeComponent();
        NameBox.Text = recording.Name;
        Loaded += async (_, _) =>
        {
            SaveButton.IsEnabled = false;
            await SubjectPicker.InitializeAsync(workspace,
                new(recording.SubjectCode, recording.SubjectName, recording.SubjectId, recording.SubjectSex, recording.AgeAtRecordingYears), recording);
            SaveButton.IsEnabled = true;
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string RecordingName => NameBox.Text.Trim();
    public RecordingSubject Subject => SubjectPicker.Model.Selection;

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ValidationText.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(NameBox.Text))
            ValidationText.Visibility = Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ValidationText.Text = "请输入有效的记录名称。";
            ValidationText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        try { _ = Subject; }
        catch (Exception exception)
        {
            ValidationText.Text = exception.Message;
            ValidationText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
