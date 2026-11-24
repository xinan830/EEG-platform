# 导联删除与现存引用验收

日期：2026-10-10。仅修复导联预设的引用生命周期，不修改 EEG、采集快照、
通道 ID 筛选、参考公式或算法来源。普通记录删除是移入项目回收目录，
不是永久删除；本修复不会自动清空任何用户记录或回收目录。

## 根因与修复路径

旧索引仅累加曾使用过的导联 ID，编辑与删除共用永久锁。因此即使最后
一份记录彻底删除，导联仍不能删除。归档不是根因。

`MontageReferenceIndex` 将历史 ID 与现存引用分离，保留已知扫描根目录
和登记记录路径。`MontageRecordingReferences` 扫描活动记录、回收目录及
登记的外部记录；旧数组迁移后仍保留不可覆盖的历史身份。
`MontageProfileStore` 在存储边界重新检查删除条件，采集 manifest 发布与
删除共用串行锁。界面绑定 `HasRecordingReferences`，刷新后重新显示删除权限。

| 状态 | 编辑同一 ID | 删除用户预设 |
| --- | --- | --- |
| 未使用 | 可以（归档除外） | 可以 |
| 存在活动或可恢复回收记录 | 不可以 | 不可以 |
| 最后一份可发现引用已永久删除且验证成功 | 不可以 | 可以，包括已归档预设 |
| 索引损坏、记录无法验证、磁盘不可访问 | 阻止不安全修改 | 不可以 |

## 自动化证据

`MontageReferenceReleaseTests` 覆盖：永久删除后解除删除锁、旧 ID 不可复用、
归档删除、软删除与恢复、多记录、旧数组迁移、项目移除、外部记录重启、
损坏 manifest/索引、缺失 manifest 但仍有 raw 文件、不可访问卷、取消刷新、
路径末尾分隔符、冲突身份、未定位引用、采集发布并发、失败发布及界面刷新。
测试文件、目录移动和删除全部使用临时夹具，不删除用户真实数据。

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --filter "FullyQualifiedName~Montages|FullyQualifiedName~LocalAcquisitionRawWriterTests|FullyQualifiedName~AcquisitionCoordinatorTests|FullyQualifiedName~ChannelConfigurationReferencePolicyTests" --artifacts-path _verify/montage-release -p:NoWarn=NU1701
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-build --filter "(FullyQualifiedName~RecordingReviewViewModelTests|FullyQualifiedName~RecordingReviewSessionTests)&FullyQualifiedName!~Canvas" --artifacts-path _verify/montage-release -p:NoWarn=NU1701
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-build --filter "FullyQualifiedName~RecordingReviewViewModelTests.Canvas" --artifacts-path _verify/montage-release -p:NoWarn=NU1701
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --configuration Release --artifacts-path _verify/montage-release -p:NoWarn=NU1701
openspec validate release-unused-montage-references --strict
openspec validate freeze-referenced-montages --strict
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
git diff --check
```

相关分组分别为 74、27、5 项通过；Release 构建 0 警告、0 错误。
OpenSpec 严格校验、代码规模策略和差异检查通过。
新增索引/扫描/存储及测试文件低于 400 行；主窗口 439/445 行，
导联工作区 809/830 行，均在已登记上限内，没有扩大工作区职责。

完整桌面套件曾运行 558 项：530 通过、28 失败，发生于最后两项补充测试
加入前。失败输出包含 WPF 资源 URI 初始化和“应用程序对象正在关闭”；
画布测试独立进程 5/5 通过。未建立干净基线证明失败与本次改动无关，
不宣称全套通过，也不在此变更扩大修改图表测试框架。

## 手工验收

1. 使用新构建重启桌面应用，重新进入导联列表以刷新引用。
2. 有活动记录的导联仍只读且不可删除；移动记录到项目回收目录后仍不可删除。
3. 使用确认不再需要的测试记录，永久删除其活动与回收位置的最后一份引用。
   不要删除真实研究记录来验证此规则。
4. 重新进入导联列表：对应用户预设可删除，已归档预设也可删除；原 ID
   不恢复可编辑。复制为新 ID 仍可用。
5. 有其他记录引用时仍应拒绝删除；原始快照、同通道兼容切换和波形不改变。

## 迁移与限制

旧 ID 数组没有外部路径，只能从平台已知项目/回收目录重建现存引用，
不能证明全磁盘所有未知复制品都已删除。新外部回溯会登记具体位置；
移到未知目录的记录需重新打开登记。没有位置的新登记保守保持删除锁。

离线磁盘、权限异常、冲突引用或不完整记录不作为删除证据。保留历史
路径意味着验证成本随已登记范围增长；扫描只读元数据和文件名，不读 EEG。
当前只支持一个桌面进程修改这些 JSON 文件。新索引为版本 2，旧构建不能
读取；不要删除索引绕过保护或直接用旧程序回退。
