# 回溯 11 项失败修复验证（2026-11-25）

## 范围与根因

原 Review 测试为 528 passed / 11 failed / 539 total。本次只修改导航业务代码、相关测试与变更文档，保留工作区已有科学预处理修改；未提交或推送。

| 原失败 | 根因与处理 |
| --- | --- |
| 标注渲染 6 项 | 测试仍断言旧 384 DIP 界面；当前已提交界面为 416 DIP。使用当前布局进行 100%/150% 渲染。 |
| 标注文案 1 项 | 当前为“事件标注”与双页签、宫格、历史行按钮；测试仍查旧名称及已删除的下拉框/按钮。替换为当前真实控件的行为验证，保留权限与编辑检查。 |
| 标注窗口生命周期 1 项 | 标题栏已为 48 DIP，旧测试断言 46 DIP。保留非模态、关闭、位置、编辑草稿及会话释放检查。 |
| 分析发布 1 项 | 无类型协调器 AlgorithmId=unknown 且没有选择活动面板。测试改用有身份的既有 PanelProvider，经 OpenAnalysisAsync 进入真实选择/快照校验/发布流程；不添加 RBP 回退。 |
| 拖动暂停 1 项 | BeginTimelineDrag 只停分析预取，未暂停唯一播放时钟。修复暂停/恢复状态所有权，重复通知幂等，终点不重播，关闭不恢复。 |
| 真实导出验收 1 项 | xUnit v2 测试抛出的动态跳过被当前运行器记为失败。未配置时改为发现阶段 Skip；部分配置执行并明确校验缺失项；全部配置保留原 HTTP 验收。 |

数据时间仍为录音绝对秒/样本坐标，原始数据、V/µV 边界、通道顺序、科学滤波和 HTTP/WebSocket 契约均未修改。业务改动限 RecordingReviewViewModel.Navigation.cs（232 行），本次触及文件均低于 400 行。

## 自动化结果

- Release 桌面与测试项目编译成功。
- 完整 Review：**545 passed / 0 failed / 1 skipped / 546 total**，约 19 秒；修复后两轮结果一致。新增 7 项（2 项拖动边界、5 项导出配置行为）。
- 原 11 项中，10 项通过，1 项真实 HTTP 导出因未配置明确跳过，不计为验收成功。
- 编译 WPF 窗口的空状态、事件列表、编辑页在 100%/150% 各输出图像；实际查看事件列表 100% 与编辑页 150% 图像。未运行真实应用手工交互或硬件验收。
- OpenSpec strict、代码规模策略、git diff --check 通过。构建有现存 CA2022 / xUnit2013 警告。

本地日志和 TRX 位于 gitignored 的 `.artifacts/review-regression*`，渲染图位于 `.artifacts/review-regression/`。

## 扩展完整桌面检查的剩余失败

扩大范围后：**1688 passed / 6 failed / 1 skipped / 1695 total**，约 45 秒。以下六项单独重跑仍失败，未因本次修复而屏蔽、跳过或降低断言；没有在撤销全部工作区改动的基线上验证，故不声称已证明它们是历史失败。

| 测试 | 已观察到的失败 |
| --- | --- |
| RecordingTimeChartModeTests.Rbp_time_mode_does_not_leak_into_band_categories | 图表没有预期序列，访问 RenderableSeries[0] 越界。 |
| RbpResultPreviewTests.SciChartTrend_LeavesRejectedWindowAsNanGap | 预期 5 条序列，实际 0。 |
| RbpResultPreviewTests.CurrentVersion_SciChartTrendContainsFiveSeriesIncludingGamma | 预期 5 条序列，实际 0。 |
| MontageReferenceReleaseTests.EmptyDiscoveredIdentityCanBeReconciledFromRecordingManifest | 空引用登记与 manifest 对账被判为不一致。 |
| LiveMonitoringViewModelTests.ConfigureChannels_DoesNotAutoDisplayUnnamedPhysicalInputs | 预期保留 Fp1，实际可见通道为空。 |
| CacheMaintenanceTests.LinkedDirectoryRefusesBothLocalTreesBeforeDeletingFiles | 创建测试符号链接时缺少 Windows 特权，尚未执行清理保护断言。 |

这些扩展失败不在原 Review 11 项集合内，仍需独立定位；不能据本次 Review 通过宣称完整桌面套件全绿。本次未重跑后端或真实录音算法，因为未修改科学处理或后端执行路径。
