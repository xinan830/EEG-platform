# 导联引用元数据整理验收

日期：2026-10-10。基于已推送提交 `9367394`，本轮为未提交的内部结构整理。
未改原始记录、manifest JSON、通道 ID 筛选、导联运算或引用生命周期。

## 证据、问题与修复路径

原 `MontageRecordingReferences.ReadIds` 负责扫描器与 store 的 ID 解析，
store 另外按字符串键读取快照。职责边界不够明确。

`MontageReferenceMetadata` 集中两项字段的解释；scanner 和 store 复用，
公开注册接口仍接收原字典。adapter 不负责持久化、兼容筛选或删除政策。
抽离时发现的空白快照行为差异已修正：扫描忽略空白快照；严格采集校验
在已有非空 ID 时仍拒绝存在但为空白的快照。非空 ID 不裁剪、不折叠大小写。

测试覆盖字段缺失、空白、ID-only 旧数据、snapshot-only 数据、相同身份、
冲突身份、缺少快照 ID、损坏 JSON，以及解析不修改输入元数据。
store 级测试确认严格校验失败不会注册引用。

## 复现命令与结果

在仓库根目录执行：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --filter "FullyQualifiedName~Montages|FullyQualifiedName~LocalAcquisitionRawWriterTests|FullyQualifiedName~AcquisitionCoordinatorTests|FullyQualifiedName~ChannelConfigurationReferencePolicyTests" --artifacts-path _verify/metadata-cleanup -p:NoWarn=NU1701
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --artifacts-path _verify/metadata-cleanup -p:NoWarn=NU1701
openspec validate centralize-montage-reference-metadata --strict --no-interactive
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
git diff --check
```

- 相关测试：85 通过、0 失败。首轮新增测试曾因精确异常类型断言失败，
  实际异常是 `JsonException` 的子类；仅修正断言后复跑通过。
- Release：0 警告、0 错误；命令按项目既有验证方式抑制 NU1701。
- OpenSpec 严格校验、规模检查与差异检查通过。
- adapter 49 行、store 170 行、scanner 110 行；新测试 118 行、生命周期
  测试 268 行，均低于 400 行目标。

## 边界与限制

本轮未重新运行完整 WPF 套件，不宣称全套通过。此前全套的资源初始化
及应用关闭失败见 [引用生命周期验收](2026-10-10-montage-reference-release-report.md)。
本轮未操作硬件、修改用户录制文件或清理回收目录。

递归引用验证成本、跨进程写入限制保持原状。只集中导联引用字段的内部
解释，不能视为整个平台的硬件元数据接口已经全面类型化。无需数据迁移；
撤回代码抽离即可恢复原职责位置，无需恢复或重写历史 manifest。

另行出现的回顾工具栏改动、`simplify-review-toolbar` 计划及用户临时文件
不属于本轮范围，保留原样。
