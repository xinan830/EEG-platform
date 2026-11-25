using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Projects.Views;

public sealed partial class RenameRecordingDialog : Window
{
    public RenameRecordingDialog(string currentName, string subjectCode = "", string subjectName = "", bool sharedSubject = false)
    {
        InitializeComponent();
        NameBox.Text = currentName ?? string.Empty;
        SubjectCodeBox.Text = subjectCode;
        SubjectNameBox.Text = subjectName;
        SharedSubjectNotice.Visibility = sharedSubject ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string RecordingName => NameBox.Text.Trim();
    public string SubjectCode => SubjectCodeBox.Text.Trim();
    public string SubjectName => SubjectNameBox.Text.Trim();

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

        DialogResult = true;
    }
}
