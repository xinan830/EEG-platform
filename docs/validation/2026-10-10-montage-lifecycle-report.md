# 导联版本与历史快照验收

日期：2026-10-10。范围是导联配置生命周期、历史引用和回溯选择；不改变原始 EEG、参考公式、滤波或算法计算。

后续修正：[删除失效引用验收](2026-10-10-montage-reference-release-report.md)
将删除限制改为依据现存记录与可恢复回收目录，而非永久历史 ID。
本文保留该阶段的测试证据；当前规则是历史版本不可覆盖、无现存引用可删除。

## 结论

已实现以下规则：

- 采集前选择的导联快照写入 recording manifest。
- 记录引用的导联 ID 写入桌面侧引用索引，并在已知项目及 `.trash/recordings` 中重新扫描。
- 被引用配置的名称、通道映射、参考方式、颜色、顺序和描述不能覆盖保存。
- 旧草稿在采集后再保存也会被存储层拒绝。
- 复制生成新 ID；原版本仍可查看，历史快照不被替换。
- 归档配置不再进入新的采集选择，但保留历史数据所需字段。
- 回溯默认显示采集时导联快照；仍可切换同一通道 ID 且科学映射兼容的导联。切换不改历史快照、原始记录或算法来源。
- 采集准备后删除导联，以及导联 ID 与快照 ID 冲突，均在登记引用前拒绝。
- 相同指纹的导联共享波形缓存时，图表名称随当前导联更新，不显示上一份配置的名称。

未引用的用户配置仍可编辑。引用锁定不取代通道/参考兼容性校验，也不取消回溯重参考；上一轮将回溯限制为只显示快照属于范围扩大，现已纠正。

## 证据

| 行为 | 测试 |
| --- | --- |
| 引用后重启仍只读、保存/删除被拒绝 | `MontageLifecycleTests.ReferencedIdRejectsAllEditsAndDeletionAfterRestart` |
| 旧项目和可恢复删除目录被扫描 | `LegacyProjectManifestsCreateDurableLocks` |
| 损坏历史元数据不静默解锁 | `CorruptHistoricalMetadataDoesNotSilentlyUnlockProfiles` |
| 打开旧草稿后产生记录仍不能覆盖 | `DraftOpenedBeforeRecordingCannotOverwriteOrDeleteNewlyReferencedId` |
| 复制生成新版本并保留原快照 | `ReadOnlyEditorCanCopyToNewIdWithoutChangingHistory` |
| 归档不改变指纹、颜色、顺序和公式 | `ArchiveKeepsReferencedSnapshotFieldsAndAllowsCopy` |
| 回溯兼容切换保留采集快照与播放位置 | `RecordingReviewViewModelTests.ViewModelExposesAcquisitionAndCurrentViewingMontageSeparately` |
| 删除旧预览配置后不能采集 | `DeletedPreviewPresetCannotStartRecordingOrAcquireLock` |
| 元数据 ID 与快照 ID 冲突不登记引用 | `ConflictingSnapshotIdentityCannotLockEitherProfile` |

## 验证命令

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --filter "(FullyQualifiedName~Montages|FullyQualifiedName~RecordingReviewViewModelTests|FullyQualifiedName~LocalAcquisitionRawWriterTests|FullyQualifiedName~RecordingReviewSessionTests)&FullyQualifiedName!~Canvas" --artifacts-path _verify/montage-lifecycle -p:NoWarn=NU1701
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-build --filter "FullyQualifiedName~RecordingReviewViewModelTests.Canvas" --artifacts-path _verify/montage-lifecycle -p:NoWarn=NU1701
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --filter "FullyQualifiedName~AcquisitionCoordinatorTests|FullyQualifiedName~EventServicesTests|FullyQualifiedName~ChannelConfigurationReferencePolicyTests" --artifacts-path _verify/montage-lifecycle -p:NoWarn=NU1701
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --configuration Release --artifacts-path _verify/montage-lifecycle -p:NoWarn=NU1701
openspec validate freeze-referenced-montages --strict
git diff --check
```

在 `backend/` 工作目录验证后端：

```powershell
.venv/Scripts/python.exe -m pytest tests/test_acquisition_analysis_signals.py -q
```

本轮结果：70 项非画布相关测试通过；5 项 SciChart 画布测试单独运行通过；18 项采集协调、事件及通道引用测试通过；后端采集导联来源测试 32 项通过；Release 构建 0 警告、0 错误。OpenSpec 严格校验与 `git diff --check` 通过；本轮涉及的超限 WPF 文件均在 `docs/code-size-policy.json` 已登记上限内。

上一轮完整 WPF 测试套件运行时出现 22 个 SciChart/WPF 资源初始化失败（`The URI prefix is not recognized` 或“应用程序对象正在关闭”）；相同时间轴测试单独运行通过。尚未通过干净基线对照证明其与本改动无关，不能称为全绿或直接归因于既有问题。本变更不扩大到图表测试基础设施修复。

本轮 75 项混合运行复现了其中 5 项画布资源初始化失败，同一批画布测试在独立进程中 5/5 通过。因此只报告分组通过，不掩盖混跑失败。

## 手工验收和边界

1. 用用户导联 A.1 采集并结束记录，返回导联列表：A.1 可查看/复制，不能编辑/删除。
2. 重启后 A.1 仍锁定；复制为 A.2 可调整参考、颜色和顺序，保存后 ID 不同。
3. 打开历史记录默认仍使用 A.1；同一通道配置下兼容的 A.2 可切换，不改变播放位置，关闭重开仍默认 A.1。
4. 归档 A.1 后，新采集列表不再提供它，历史记录仍可查看。

引用索引位于本地应用数据目录的 `BrainPlatform/montage-profiles.json.references.json`。已知项目和可恢复删除目录被扫描；外部记录需打开/导入后才能登记。配置与 manifest 均由当前桌面进程保护，尚不支持多个独立进程同时改写预设。记录创建中断可能留下保守锁，可复制新配置继续使用；不通过删除索引人为解锁。实际设备采集与界面观感仍需上述手工验收。
