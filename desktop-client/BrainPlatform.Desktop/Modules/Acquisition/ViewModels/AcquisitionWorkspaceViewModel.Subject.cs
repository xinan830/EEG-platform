namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed partial class AcquisitionWorkspaceViewModel
{
    private string subjectCode = string.Empty;
    private string subjectName = string.Empty;

    public string SubjectCode
    {
        get => subjectCode;
        set => SetProperty(ref subjectCode, value);
    }

    public string SubjectName
    {
        get => subjectName;
        set => SetProperty(ref subjectName, value);
    }
}
