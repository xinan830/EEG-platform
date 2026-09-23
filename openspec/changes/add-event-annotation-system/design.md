## Context

桌面端已有本地 Recording manifest、样本计数和受限窗口回溯读取能力。采集核心要求原始批次先持久化，回溯要求以样本计数和采样率作为科学时间轴。因此事件不能作为波形显示状态临时保存在 ViewModel，也不能写入原始样本文件。

## Goals

- 设置页维护全局可复用的事件类型。
- 每次采集或回溯操作产生可追溯的 RecordingEvent。
- 采集、回溯、设备 Trigger 和未来导入 Annotation 使用同一事件模型。
- 事件标记写入不能阻塞设备读循环、原始写入或波形渲染。

## Data ownership

`EventDefinition` 由桌面端用户配置存储拥有，建议沿用现有 Configuration store 的原子 JSON 写入和版本检查方式。

`RecordingEvent` 由 Recording 所属的事件元数据存储拥有，按 RecordingId 隔离。它与 raw chunks 分离，但位于同一项目 Recording 目录的受控元数据区域。

定义与记录的关系为：

```text
Project -> Recording -> RecordingEvent -> EventDefinition
```

RecordingEvent 仍需保存 `EventCodeSnapshot`、`EventNameSnapshot`、`ColorSnapshot` 和定义版本，避免定义后续编辑改变历史显示和分析语义。

## Time and units

RecordingEvent 使用：

```text
StartSample: long
DurationSamples: long
```

`DurationSamples == 0` 表示点事件；大于零表示区间事件。显示层按 Recording manifest 的 `SamplingRateHz` 计算秒数。若设备样本计数不是从零开始，存储必须同时保存 Recording 内的 sample-counter 坐标或可确定的 offset，不得用 PC 接收时间替代。

## Shortcut registry

`ShortcutRegistry` 为 `Global`、`Acquisition`、`Review` 作用域注册命令身份和快捷键。事件定义保存快捷键声明，但只有注册中心确认没有冲突且事件启用时才生效。

注册冲突必须返回结构化结果，包含已占用快捷键和占用命令；保存事件定义不得静默覆盖已有快捷键。

## Event sources

使用受限枚举：

```text
ManualButton
KeyboardShortcut
DeviceTrigger
ImportedAnnotation
Algorithm
System
```

`SourceDetail` 保存按钮、按键、设备通道或导入字段的可读细节；`ExternalCode` 保存设备/文件原始代码。

## Lifecycle rules

- 系统事件不能删除，只能按规则停用或修改允许的显示字段。
- 已被 RecordingEvent 引用的用户事件定义不能物理删除，只能停用。
- 停用定义不能出现在新采集的事件工具栏，但历史 RecordingEvent 仍可显示。
- 人工事件可以在回溯中编辑备注、起止位置和颜色快照；设备 Trigger、导入 Annotation 和系统事件默认只读。
- 事件写入失败只影响事件元数据，不得停止或回滚原始采集。

## Alternatives considered

### Put event labels into raw EEG chunks

Rejected. 这会破坏原始文件不可变约束，并使事件编辑污染科研源数据。

### Store only EventDefinitionId on RecordingEvent

Rejected. 后续修改定义名称或颜色会改变历史记录的解释，无法满足追溯要求。

### Separate acquisition and review event models

Rejected. 两套模型会造成快捷键、来源、时间坐标和颜色解释不一致。

## Migration and rollback

新存储文件应带独立 schema version，并使用临时文件加原子替换。回滚时保留事件文件，不影响旧 Recording 读取；关闭事件 UI 不应删除事件元数据。实现阶段先增加读写和测试，再接入页面，避免页面先产生无法读取的事件。

## Testing strategy

- 模型校验：Code、名称、颜色、快捷键和点/区间持续时间。
- 存储测试：原子写入、重启恢复、无事件旧记录、损坏文件错误。
- 时间测试：样本点到秒数、非零首样本计数、区间边界和采样率变化拒绝。
- 快捷键测试：全局/采集/回溯作用域冲突和停用事件释放。
- 采集集成测试：标记不阻塞原始写入和设备读循环。
- 回溯集成测试：加载、跳转、编辑和删除事件不改变 raw chunks。
