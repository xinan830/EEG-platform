# 实时采集算法协调验收（2026-11-20）

本次将实时 RBP 的调度边界整理为采集专用的通用窗口算法协调器。采集与离线回溯分别拥有执行协调器；离线 Run、科学计算协调器和回溯调度代码未修改。原始写盘、显示波形和分析传输分别运行，算法结果仅在采集界面临时展示。

## 当前链路和接入边界

```text
设备采集
  ├─ 原始数据写盘
  ├─ 波形显示
  └─ 有界样本传输 → LiveRecordingAccessSession
                      → LiveAnalysisCoordinator
                          → 注册的窗口算法适配器（首个为 RBP）
                      → 通用结果信封 → 算法呈现适配器 → 现有图表
```

- 数据访问沿用实时样本会话：float64、sample-major、V，通道名与样本列顺序对应。协调器在工作线程读取拥有独立数组的 SampleBlock；缺失或已覆盖的区间在算法计算之前拒绝。
- 科学窗口由采样计数器定义，采用 `[start_sample, end_sample)`。传输批次长度和 HTTP 到达时间不定义窗口。启用算法时已有样本不参与计算；秒数相对该算法启用后的首个样本，样本边界保留真实计数器坐标。
- 全局最多四个工作槽，每个算法实例最多一个计算窗口。落后的窗口合并到最新对齐窗口并报告 skipped_windows；繁忙时显式返回 overloaded，不建立无限队列。
- 单算法异常、停止、重新启用、采集会话关闭都有独立边界。旧实例和旧 revision 的结果不能覆盖当前结果。已经进入原生计算的调用无法强制终止，其工作槽在实际完成后释放。
- 新增无状态窗口算法需要实现 configure/calculate、注册适配器，并接入对应呈现器及 UI 控件。无需修改设备读循环或通用传输协议。状态流式算法、事件触发算法和跨算法依赖图不属于当前适配器契约。
- RBP 沿用五频段计算原语、质量门、单位及 provenance；使用原有共享图表。实时实现保持 rbp-live-v1 / rbp-live-window-v1，窗口局部零相位处理不声明与离线 official RBP 数值完全等价。通用缺口和过载映射为不可用图表点，不用零值代替。

## 当前消费者及协议迁移

唯一当前消费者为桌面 HttpLiveSampleAccessBridge。后端与桌面端需一起更新并重启。

- `POST/GET/DELETE /api/live-recordings/sessions/{session_id}/algorithms/{algorithm_id}` 负责启用、最新结果和停止。
- 启用请求使用 `live-analysis-config-v1`，其中 config 由算法自己定义。
- 结果使用 `live-analysis-update-v1`，包含会话、算法、revision、状态、样本/秒边界及算法 payload。
- 样本接收应答升级为 `live-sample-ack-v2`，通过 updates 数组传递各算法结果。
- 移除未发布的 RBP 专用调度路径，不保留旧格式读取。没有数据库迁移，也不修改已有记录文件。

## 自动化验证

| 检查 | 结果与范围 |
| --- | --- |
| 后端相关测试 | 80 通过；包含通用契约、协调器、HTTP、实时数据访问/滤波、RBP 科学回归及离线按需协调相关测试。 |
| API 最终补测 | 为样本应答路由增加 response_model 后，test_live_analysis_api.py 再次 6 通过。 |
| 桌面采集测试 | 215 通过；含多个算法独立启停、启动失败隔离、会话与 revision 校验、RBP 投影及图表历史。 |
| 桌面采集与回溯组合回归 | 677 通过、3 失败；失败原因及原始提交复现见下文。 |
| OpenSpec | generalize-live-analysis-coordination 与 acquisition-live-rbp-analysis 均通过 strict 校验。 |
| 差异检查 | git diff --check 通过。 |
| 变动生产文件规模 | 对本次工作区改动的 Python/C#/XAML 使用现有 assess_file 策略检查，0 失败。 |
| 全库规模检查 | 未通过：未修改的 backend/app/algorithms/psd/runner.py 为 464 行，超过原登记的 420 行。 |

模拟流覆盖：不同传输批次落在同一样本窗口网格、启用边界、缺口与恢复、读取前保留区覆盖、突发窗口跳过、全局及单算法过载、故障隔离、数据源替换、停止/重启、逆序完成及运行中关闭会话。固定输入的 RBP 回归校验数值、通道顺序、单位、质量、provenance 与身份。

### 回溯失败的基线对照

从 HEAD `acf97e464e3a55b0129b95d2c8ec0183d60bf92a` 导出独立源码到 `artifacts/live-coordination-baseline`，使用独立构建目录重新运行下列测试，原始提交出现相同的 3 个失败，另一个 wasPlaying=false 分支通过：

1. `Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`：原有断言期望 Bands 通知 1 次，实际 0 次。
2. `Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`：原有拖动后 IsPlaying 断言失败。
3. `Provided_recording_exports_through_real_http_client_when_acceptance_is_configured`：未配置 BRAIN_PLATFORM_EXPORT_RECORDING/OUTPUT/BACKEND，当前运行器把动态跳过作为失败报告。

这些失败未在本次采集架构变更中修复；不能据此声称回溯套件全部通过。

### 主要复现命令

```powershell
backend/.venv/Scripts/python.exe -m pytest backend/tests/test_live_analysis_contracts.py backend/tests/test_live_analysis_coordinator.py backend/tests/test_live_analysis_api.py backend/tests/test_live_rbp_analysis.py backend/tests/test_live_recording_access.py backend/tests/test_live_filter_api.py backend/tests/test_live_filter_service.py backend/tests/test_rbp_five_band_contract.py backend/tests/test_rbp_channel_quality.py backend/tests/test_rbp_execution_parity.py backend/tests/test_rbp_on_demand_capability.py backend/tests/test_on_demand_rbp_api.py backend/tests/test_on_demand_dynamic_service.py backend/tests/test_scientific_compute_coordinator.py -q

dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --artifacts-path artifacts/live-coordination-verification --no-restore --filter 'FullyQualifiedName~Acquisition' --verbosity minimal

dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --artifacts-path artifacts/live-coordination-verification --no-restore --filter 'FullyQualifiedName~Acquisition|FullyQualifiedName~Tests.Review' --verbosity quiet

openspec validate generalize-live-analysis-coordination --strict
openspec validate acquisition-live-rbp-analysis --strict
git diff --check
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
```

## 规模评审及验收限制

新增生产文件均低于 400 行。已有采集工作区 ViewModel 为 1186 行，保留 docs/code-size-policy.json 中可执行的设备配置、实时分析呈现、事件命令拆分计划；运行时为 448 行。没有为本次协议整理展开整页生命周期重写。

本次未控制电脑、未停止用户正在运行的程序，构建输出使用独立 artifacts 目录。尚未使用真实放大器做长时间采集、内存/CPU 压测或人工图表交互验收；本报告证明自动化边界和回归结果，不证明硬件连续采集表现。

## 2026-11-21 滚动窗口验证

预览和录制共用一个实时样本会话。从第一批起，现有异步分发器按原通道顺序上传 V 单位的 `float64` 样本。后端保留 `max(30_000, 12 × 采样率)` 个样本。RBP 开关只控制算法，重新开启时沿用采集首样本作为时间零点。

第 23 秒开启 RBP 时，用最新已接收样本调度 `[13s, 23s)`，再按一秒步长调度 `[14s, 24s)`。第 4 秒开启则在采满 10 秒时得到 `[0s, 10s)`。缺口与覆盖仍产生不可用结果。持续采集时结果随样本应答交付。启用后最多查询最新结果 5 秒，每次查询间隔 200 毫秒，首结果、停用或会话关闭会取消查询。沿用现有 HTTP 消息、RBP 核心计算和结果图表。

| 检查 | 结果 |
| --- | --- |
| 后端聚焦测试 | 82 通过。覆盖滚动窗口、缓存覆盖、采样计数偏移、缺口、过载、启停、HTTP 首结果和 RBP 数值回归，也包含离线按需计算回归。 |
| 桌面采集测试 | 218 通过。验证从首个预览批次开始传输，转入录制后会话连续、原始文件起点正确，也验证无需新批次即可接收首结果以及停用和关闭取消查询。 |
| 桌面采集与回溯组合 | 680 通过、3 失败。失败项与上方记录的 HEAD 基线一致。 |
| OpenSpec | `generalize-live-analysis-coordination` 和 `acquisition-live-rbp-analysis` 通过严格校验。 |
| 文件规模 | 变动的 20 个生产文件全部符合已登记策略。全库检查仍报告已知的 PSD `runner.py` 超限。 |

任务 5.5 的实机验收待完成。使用放大器连续录制，比较 RBP 关闭、在约第 23 秒开启和再次开启时的首窗、后续一秒步长、原始采样计数与文件回放。同步观察 RBP 关闭时仍然上传样本带来的 CPU 和内存开销。
