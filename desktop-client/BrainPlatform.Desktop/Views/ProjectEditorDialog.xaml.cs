using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Projects;
using BrainPlatform.Desktop.ViewModels;
using Microsoft.Win32;

namespace BrainPlatform.Desktop.Views;

public partial class ProjectEditorDialog : Window
{
    private readonly ProjectEditorDialogViewModel viewModel;

    public ProjectEditorDialog(ResearchProject? project = null)
    {
        InitializeComponent();
        viewModel = new ProjectEditorDialogViewModel(project);
        DataContext = viewModel;
    }

    public ResearchProjectDraft Draft => new(
        viewModel.Name,
        viewModel.Description,
        viewModel.Purpose,
        viewModel.TagsText,
        viewModel.Creator,
        viewModel.DirectoryPath,
        viewModel.Status,
        viewModel.Notes);

    private void OnBrowseDirectoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择项目目录",
            Multiselect = false,
            InitialDirectory = Directory.Exists(viewModel.DirectoryPath)
                ? viewModel.DirectoryPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) == true)
        {
            viewModel.DirectoryPath = dialog.FolderName;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        viewModel.AddTagInput();
        if (string.IsNullOrWhiteSpace(viewModel.Name))
        {
            viewModel.ErrorMessage = "请输入项目名称。";
            return;
        }

        if (string.IsNullOrWhiteSpace(viewModel.DirectoryPath))
        {
            viewModel.ErrorMessage = "请选择项目目录。";
            return;
        }

        DialogResult = true;
    }

    private void OnTagInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        viewModel.AddTagInput();
        e.Handled = true;
    }

    private void OnRemoveTagClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag) viewModel.RemoveTag(tag);
    }
}

public sealed class ProjectEditorDialogViewModel : ObservableObject
{
    private string name;
    private string description;
    private string purpose;
    private string tagInput = string.Empty;
    private string creator;
    private string directoryPath;
    private string status;
    private string notes;
    private string errorMessage = string.Empty;

    public ProjectEditorDialogViewModel(ResearchProject? project)
    {
        DialogTitle = project is null ? "新建项目" : "项目设置";
        name = project?.Name ?? string.Empty;
        description = project?.Description ?? string.Empty;
        purpose = project?.Purpose ?? string.Empty;
        foreach (var tag in project?.Tags ?? []) Tags.Add(tag);
        creator = project?.Creator ?? Environment.UserName;
        directoryPath = project?.DirectoryPath ?? string.Empty;
        status = ResearchProjectStatuses.Normalize(project?.Status);
        notes = project?.Notes ?? string.Empty;
    }

    public string DialogTitle { get; }
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Description { get => description; set => SetProperty(ref description, value); }
    public string Purpose { get => purpose; set => SetProperty(ref purpose, value); }
    public ObservableCollection<string> Tags { get; } = [];
    public string TagInput { get => tagInput; set => SetProperty(ref tagInput, value); }
    public string TagsText => string.Join("，", Tags);
    public string Creator { get => creator; set => SetProperty(ref creator, value); }
    public string DirectoryPath { get => directoryPath; set => SetProperty(ref directoryPath, value); }
    public IReadOnlyList<string> StatusOptions => ResearchProjectStatuses.FilterOptions.Skip(1).ToArray();
    public string Status { get => status; set => SetProperty(ref status, ResearchProjectStatuses.Normalize(value)); }
    public string Notes { get => notes; set => SetProperty(ref notes, value); }
    public string ErrorMessage { get => errorMessage; set => SetProperty(ref errorMessage, value); }

    public void AddTagInput()
    {
        var values = TagInput.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var value in values)
        {
            if (!Tags.Contains(value, StringComparer.OrdinalIgnoreCase)) Tags.Add(value);
        }
        TagInput = string.Empty;
        RaisePropertyChanged(nameof(TagsText));
    }

    public void RemoveTag(string tag)
    {
        var existing = Tags.FirstOrDefault(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return;
        Tags.Remove(existing);
        RaisePropertyChanged(nameof(TagsText));
    }
}
