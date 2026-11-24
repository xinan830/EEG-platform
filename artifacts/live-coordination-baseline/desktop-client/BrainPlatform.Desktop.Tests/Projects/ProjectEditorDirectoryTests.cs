using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Projects;

[Collection("WPF application resources")]
public sealed class ProjectEditorDirectoryTests(WpfApplicationFixture application)
{
    [Fact]
    public void NameChangesUpdatePreviewWithoutCreatingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "project-editor-tests", Guid.NewGuid().ToString("N"));
        var policy = new ProjectDirectoryPolicy(root);
        var editor = new ProjectEditorDialogViewModel(null, policy);
        var changes = new List<string?>();
        editor.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        Assert.Empty(editor.DirectoryPathPreview);
        Assert.False(editor.Validate());
        editor.Name = "实验一";
        Assert.Equal(Path.Combine(root, "data", "实验一"), editor.DirectoryPathPreview);
        Assert.True(editor.Validate());
        editor.Name = "实验二";
        Assert.Equal(Path.Combine(root, "data", "实验二"), editor.DirectoryPathPreview);
        Assert.Contains(nameof(editor.DirectoryPathPreview), changes);
        Assert.Empty(editor.DirectoryPath);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void CustomSelectionRemainsFixedWhenNameChanges()
    {
        var custom = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var editor = new ProjectEditorDialogViewModel(null) { Name = "实验一", DirectoryPath = custom };

        editor.Name = "实验二";

        Assert.Equal(custom, editor.DirectoryPathPreview);
        Assert.Equal(custom, editor.DirectoryPath);
        Assert.Contains("所选目录", editor.DirectoryHint);
        Assert.True(editor.Validate());
        Assert.False(Directory.Exists(custom));
    }

    [Fact]
    public void InvalidDefaultNameIsReportedButCustomLocationAllowsDisplayName()
    {
        var editor = new ProjectEditorDialogViewModel(null) { Name = "实验:一" };

        Assert.Empty(editor.DirectoryPathPreview);
        Assert.False(editor.Validate());
        Assert.Contains("文件夹名称", editor.ErrorMessage);
        editor.DirectoryPath = Path.Combine(Path.GetTempPath(), "custom-project");
        Assert.True(editor.Validate());
        Assert.Empty(editor.ErrorMessage);
    }

    [Fact]
    public void ExistingProjectRenameKeepsDirectoryInEditor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "existing-project");
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject("id", "P1", "旧名", "", "", [], "tester", directory, now, now);
        var editor = new ProjectEditorDialogViewModel(project) { Name = "新名" };

        Assert.Equal(directory, editor.DirectoryPathPreview);
        Assert.Equal(directory, editor.DirectoryPath);
        Assert.True(editor.Validate());
    }

    [Fact]
    public void CompiledDialogPreviewsPathButDraftRetainsAutomaticModeOnCancel() => application.Run(() =>
    {
        var dialog = new ProjectEditorDialog { ShowActivated = false, Opacity = 0 };
        try
        {
            dialog.Show();
            var editor = Assert.IsType<ProjectEditorDialogViewModel>(dialog.DataContext);
            var nameBox = Assert.IsType<TextBox>(dialog.FindName("NameBox"));
            nameBox.Text = "preview-" + Guid.NewGuid().ToString("N");
            nameBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            dialog.UpdateLayout();
            var pathBox = Assert.IsType<TextBox>(dialog.FindName("DirPathBox"));
            pathBox.GetBindingExpression(TextBox.TextProperty)!.UpdateTarget();
            Assert.True(pathBox.IsReadOnly);
            Assert.Equal(BindingMode.OneWay, BindingOperations.GetBinding(pathBox, TextBox.TextProperty)!.Mode);
            Assert.Equal(editor.DirectoryPathPreview, pathBox.Text);
            Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data", editor.Name), pathBox.Text);
            Assert.True(editor.Validate());
            Assert.Empty(dialog.Draft.DirectoryPath);
            Assert.False(Directory.Exists(pathBox.Text));
        }
        finally { dialog.Close(); }
    });
}
