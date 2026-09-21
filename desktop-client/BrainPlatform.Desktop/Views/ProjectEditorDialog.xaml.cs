using System.IO;
using System.Windows;
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
}

public sealed class ProjectEditorDialogViewModel : ObservableObject
{
    private string name;
    private string description;
    private string purpose;
    private string tagsText;
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
        tagsText = project is null ? string.Empty : string.Join("，", project.Tags);
        creator = project?.Creator ?? Environment.UserName;
        directoryPath = project?.DirectoryPath ?? string.Empty;
        status = ResearchProjectStatuses.Normalize(project?.Status);
        notes = project?.Notes ?? string.Empty;
    }

    public string DialogTitle { get; }
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Description { get => description; set => SetProperty(ref description, value); }
    public string Purpose { get => purpose; set => SetProperty(ref purpose, value); }
    public string TagsText { get => tagsText; set => SetProperty(ref tagsText, value); }
    public string Creator { get => creator; set => SetProperty(ref creator, value); }
    public string DirectoryPath { get => directoryPath; set => SetProperty(ref directoryPath, value); }
    public IReadOnlyList<string> StatusOptions => ResearchProjectStatuses.FilterOptions.Skip(1).ToArray();
    public string Status { get => status; set => SetProperty(ref status, ResearchProjectStatuses.Normalize(value)); }
    public string Notes { get => notes; set => SetProperty(ref notes, value); }
    public string ErrorMessage { get => errorMessage; set => SetProperty(ref errorMessage, value); }
}
