# Brain Platform 交接文档

更新时间：2026-10-27（Asia/Shanghai）

本文面向没有当前会话上下文的新会话。源码、测试和 OpenSpec 是事实来源；本文只记录工作摘要。自动化验证不等于真实 EEG 录音上的人工验收。

## 当前任务

继续完善 TNK 脑指标按需分析的流式结果管线，让波形回顾不等待算法，算法异步逐窗计算并按真实窗口时间发布结果。同时完善通道/电极位置配置、导联方向展示，以及回顾播放节奏和性能诊断。

用户最关注的症状是：不开启按需分析时波形播放正常，开启 TNK 按需分析后出现约一秒一顿的卡顿。不能只凭“关闭后端后不卡”就断言算法公式计算慢；还需区分 HTTP/轮询、线程池、Dispatcher、共享锁、集合通知及 SciChart 重绘的影响。

## 本次会话完成情况

### 擦除带尝试已撤回

用户要求撤回“固定屏幕＋擦除带”方案。截图所列擦除带专用文件已删除；导航、SciChart、测试和 cadence 文档已恢复到 `origin/main`。逐文件复查确认：仍存在的截图文件与远端一致，其他截图文件本地和远端均不存在。

### 已提交并推送

```text
8426062 Implement streamed TNK playback and electrode mapping
```

推送目标为 GitHub `origin/main`，推送成功；提交后 `main`、`origin/main`、`origin/HEAD` 均指向 `8426062`。

提交共 109 个文件，包含：

- TNK 按需播放事件、逐窗投递、前端历史追加和诊断；
- 按需分析动态窗口及后端相关调整；
- 电极位置 API、标准位置解析、手动位置覆盖和 ANT 配置关联；
- 电极位置地图、导联方向地图；
- 回顾播放刷新门控、诊断和验证记录；
- 对应测试及 OpenSpec change/spec。

提交前修复了 `ElectrodeMapProjection.cs` 中 `IsValidMapPosition` 的重复定义，并按当前坐标投影比例修正相应测试期望值。

## 验证结果与遗留问题

已通过：

- 后端 TNK、播放事件/投递、电极位置相关测试：30 项；
- 桌面电极、通道、导联方向相关聚焦测试：25 项；
- 桌面主项目 Release 构建：成功，0 错误；
- 本次相关 OpenSpec change 和 `desktop/electrode-locations` spec 严格校验通过；
- 推送成功，最新提交为 `8426062`。

未通过或未确认：

- 桌面全量测试曾失败：`MontageReferenceReleaseTests.EmptyDiscoveredIdentityCanBeReconciledFromRecordingManifest`。后续单独重跑时遇到 WPF 生成文件被另一个并行测试进程占用，未得到干净的单测结果。下一会话应单进程重跑并确认是否为既有失败。
- 尚未在真实录音上人工验收波形连续性或 TNK 是否追上游标，因此不能宣称“一秒一顿”已解决。
- 打包版 Python 后端 smoke test 曾报告 `Packaged backend health check timed out`，需与源码分析和桌面播放卡顿分开追踪。
- SDK 预览版提示、OpenTK/OpenTK.GLWpfControl/SkiaSharp.Views.WPF 的 NU1701 是构建警告，不是本次构建错误。

本地还留有未提交的测试产物目录 `desktop-client/BrainPlatform.Desktop.Tests/TestResults/`，其中有 `.dmp` 和 `.xml`。它们不属于源码提交，不要误提交或当作应用日志。

## 下一步

1. 查看 `git status --short` 和 `git log -1 --oneline --decorate`，确认提交后状态；不要重置或清理用户文件。
2. 不并行启动多个 `dotnet test`。先单独重跑失败的 `MontageReferenceReleaseTests.EmptyDiscoveredIdentityCanBeReconciledFromRecordingManifest`，再决定是否运行完整桌面测试。
3. 使用用户的真实数据 `D:\迅雷下载\纪昱彤\recordings\6.16高发展` 对比仅波形与波形＋TNK 按需分析两种情况，记录游标 tick 间隔和 TNK 结果时间。
4. 卡顿仍存在时，开启性能诊断并查看桌面日志，区分算法排队、请求轮询、Dispatcher 更新、集合通知和 SciChart 绘制耗时；不能预设后端公式为根因。

   ```powershell
   $env:BRAIN_REVIEW_PERF_DIAGNOSTICS = '1'
   $env:BRAIN_REVIEW_PERF_SCENARIO = 'waveform_only'
   ```

5. 继续验证流式投递契约：结果按 `window_id`、`window_start_s`、`window_end_s` 排序/去重；切换会话后丢弃旧结果；`latest-only` 只作用于显示目标。有状态科学算法不得无条件跳过依赖窗口。
6. 完成真实录音验收后再考虑推广到其他算法。跨窗状态复用需以数值等价、边界和恢复测试为依据。

## 交接经验与工程约束

- `git pull` 不会覆盖未提交文件。若用户要求用远端覆盖一组文件，逐个核对后使用 `git restore --source=origin/main --worktree -- <path>`；禁止对整个工作区执行 `git reset --hard`。
- 波形与算法逻辑分离，不代表运行时没有耦合；Dispatcher、同步等待、锁、HTTP 轮询、集合通知和绘图都可能造成波形卡顿。
- 流式发布主要降低首结果可见延迟，不保证降低总计算量。要分别测量排队、窗口准备、科学计算、发布、绑定和绘图。
- 晚到结果必须落在它自己的分析窗口时间上，不能按当前游标时间追加。无效结果用结构化不可用状态表达，不得填零或静默复用旧值。
- EEG 科学计算和质量判定归后端；WPF 负责交互、展示和时间轴投影，不复制科学公式。
- 内部 EEG 时序信号为 `(samples, channels)`、`float64`、伏特 V；API 波形值为微伏 µV。文件时间用秒，通道名与数据列顺序必须同步。
- 业务文件以 400 行为软上限；超出需按职责评估并维护 `docs/code-size-policy.json`，不得为缩短文件而机械拆分。
- OpenSpec 当前契约和测试证据优先于旧聊天结论。严格区分“源码/自动化测试通过”和“真实录音人工验收通过”。

## 重要路径

- TNK 后端：`backend/app/algorithms/tnk/`
- 按需分析：`backend/app/services/on_demand_dynamic.py`、`backend/app/services/on_demand_worker.py`
- TNK 事件和投递：`backend/app/algorithms/tnk/playback_event.py`、`backend/app/algorithms/tnk/playback_delivery.py`、`backend/app/services/tnk_playback_delivery.py`
- TNK 桌面展示：`desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/`
- 回顾波形与诊断：`desktop-client/BrainPlatform.Desktop/Modules/Review/`
- 电极位置 API/桌面模块：`backend/app/api/electrode_locations.py`、`backend/app/services/electrode_locations.py`、`desktop-client/BrainPlatform.Desktop/Modules/Channels/`
- 当前远端提交：`8426062`
