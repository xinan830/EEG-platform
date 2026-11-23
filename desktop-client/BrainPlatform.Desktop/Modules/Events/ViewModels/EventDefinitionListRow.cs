using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Events.Services;

namespace BrainPlatform.Desktop.Modules.Events.ViewModels;

public enum EventReferenceStatus
{
    Checking,
    Referenced,
    Unreferenced,
    Unconfirmed,
}

public sealed class EventDefinitionListRow : ObservableObject
{
    private EventReferenceStatus referenceStatus = EventReferenceStatus.Checking;

    public EventDefinitionListRow(EventDefinition definition)
    {
        Definition = definition;
        IconImage = EventIconAssetService.TryLoadImage(definition.IconPath);
    }

    public EventDefinition Definition { get; }

    public ImageSource? IconImage { get; }

    public bool HasIcon => IconImage is not null;

    public string KindBadgeText => Definition.IsIntervalKind
        ? $"持续 · {Definition.EffectiveStartLabel} → {Definition.EffectiveEndLabel}"
        : string.Empty;

    public bool IsIntervalKind => Definition.IsIntervalKind;

    public EventReferenceStatus ReferenceStatus
    {
        get => referenceStatus;
        set
        {
            if (!SetProperty(ref referenceStatus, value)) return;
            RaisePropertyChanged(nameof(ReferenceStatusText));
            RaisePropertyChanged(nameof(CanDelete));
            RaisePropertyChanged(nameof(DeleteRestrictionText));
        }
    }

    public string ReferenceStatusText => ReferenceStatus switch
    {
        EventReferenceStatus.Checking => "检查中",
        EventReferenceStatus.Referenced => "已被引用",
        EventReferenceStatus.Unreferenced => "未引用",
        _ => "未确认",
    };

    public bool CanDelete => !Definition.IsSystem && ReferenceStatus == EventReferenceStatus.Unreferenced;

    public string DeleteRestrictionText => Definition.IsSystem
        ? "系统预设不可删除"
        : ReferenceStatus switch
        {
            EventReferenceStatus.Referenced => "该事件已被历史记录引用，只能停用，不能删除",
            EventReferenceStatus.Unreferenced => "删除事件",
            _ => "历史引用尚未确认，暂不能删除",
        };
}
