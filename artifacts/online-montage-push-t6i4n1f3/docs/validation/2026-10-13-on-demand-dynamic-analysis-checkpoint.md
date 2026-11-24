# 按需动态分析阶段验收记录（2026-10-13）

## 当前状态校正

以下原有章节是同日较早的检查点，不代表当前缺口。最新证据见
[PSD 验证报告](2026-10-13-on-demand-psd-validation-report.md) 与
[OpenSpec tasks](../../openspec/changes/archive/2026-10-16-on-demand-dynamic-analysis/tasks.md)。

截至最后一轮：backend 604/604，WPF 611/611，Release 0 errors（6 个既有 NU1701），
strict/file-size/diff 均通过。retry generation、真实 OS kill/restart 自主恢复、详情 target
adapter、真实 WPF 文件 114 窗口等价和小时资源测试已补齐。
早期 WPF 资源失败的根因是测试 fixture 关闭了进程共享 Application；已修复并全量通过，
不再作为“环境失败”排除。当前未完成的是 P3 GUI 和 P4 其他算法独立迁移，开关仍默认关闭。

## 早期已验证证据

| 范围 | 命令/证据 | 结果 |
| --- | --- | --- |
| 后端全量 | `backend/.venv/Scripts/python.exe -m pytest -q` | 540 passed，2 个依赖弃用警告 |
| 按需服务与存储 | `pytest tests/test_on_demand_dynamic_service.py tests/test_dynamic_window_repository.py tests/test_on_demand_migration.py` | 24 passed |
| WPF Review/按需 | `dotnet test ... --filter "ReviewOnDemandAnalysisTests\|ReviewAnalysisBindingTests\|RecordingReview"` | 42 passed |
| WPF Release build | `dotnet build BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --no-restore` | 0 warnings，0 errors |
| WPF 全量测试 | `dotnet test BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore` | 574 passed，4 个既有 WPF/SciChart 资源初始化失败；按需/Review 分组 42 passed |
| OpenSpec | `openspec validate on-demand-dynamic-analysis --strict` | valid |
| 差异检查 | `git diff --check` | 通过；仅 CRLF 转换提示 |
| 文件规模 | `backend/scripts/check_file_sizes.py` | 新增按需源码均在策略范围内；已有 4 个登记的超 400 行文件 |

本阶段还修复了一个合同回归：`cursor_s` 必须位于按需 `DynamicWindowPlanResponse`，旧
`WindowPreviewResponse` 保持非空 `double`。WPF 现在分别暴露目标游标、实际显示游标、
后端科学状态和结构化原因；PSD provider 保留 `rejected`、`insufficient_data`、
`cancelled`、`failed`，不再全部压成 `unavailable`。

## 已覆盖的恢复/并发行为

- 同一科学窗口的并发 ensure 共享一个窗口和一个活动 attempt，各请求拥有独立 subscription。
- 一个订阅取消时，仍有其他订阅者的计算继续；最后一个订阅者退出才允许取消排队/计算窗口。
- 服务启动删除没有被 READY 窗口引用的 NPZ/JSON 孤儿文件；临时文件也会回收。
- checksum、shape、unit、finite 数组校验失败时不会返回科学数组，API 返回结构化损坏错误。

## 尚未声称完成的项

这些项目需要额外实现或真实环境证据，不能由当前单元测试推断：

1. 终态窗口的正式 retry generation/新窗口身份语义；当前终态窗口保持不可重开，避免覆盖不可变结果。
2. 进程崩溃后自动重建完整 worker 输入并重新排队；当前 lease 恢复会在下一次 ensure 时接管。
3. 算法详情页的按需 target adapter；当前按需垂直切片已接入 Review，其他详情页继续完整 Run。
4. 真实用户 EEG、gap、尾窗、非零首 sample counter、RecordingService 文件链路的 PSD 对照。
5. 小时级服务链路的 CPU、峰值内存、临时磁盘、HTTP 请求量和 WPF GUI 手测。
6. STFT、RBP、FAA、IAPF、Brainbeat、Theta/Beta、频段比值/峰频率的单窗科学迁移；能力矩阵已明确它们继续旧完整动态 Run。

## 回退与验收边界

后端 `BRAIN_ENABLE_ON_DEMAND_PSD` 默认关闭，WPF 只有在
`BRAIN_WPF_ON_DEMAND_PSD=1` 时才选择按需 PSD；关闭开关仍使用旧完整 Run。原始记录、
历史 Run 和已有 artifact 未被修改。真实 GUI 和真实记录验收应由用户在本机开启开关后执行，
并核对目标窗口状态、实际窗口范围、质量原因和播放流畅性。
