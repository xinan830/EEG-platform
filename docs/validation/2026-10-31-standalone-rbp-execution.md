# 独立 RBP 执行通道整改验证

日期：2026-10-31。范围：独立官方算法 `rbp`，不涉及 TNK 生产实现、波形或实时采集。

## 执行边界

RBP 原本已注册到 `AlgorithmRuntime`，全量与按需任务在应用装配中复用 `ScientificComputeCoordinator`。本次没有新增调度器、私有队列或单节点算法组，而是删除静态、动态全量、按需窗口各自重复的读取与计算路径，统一到 `algorithms/rbp/frame.py`。

每帧按显式通道顺序批量准备一次频谱输入，再逐通道执行 Welch 和未修改的五频段公式。各入口共享数值、质量规则、通道失败原因和频谱证据；失败保持不可用值，不补零。全量只在所有窗口质量均为 clean 时报告 clean。公共结果校验新增显式 `channel_set` 声明，仍拒绝重复通道、顺序错误及通道维度错误，固定角色算法保留原计数约束。

用户验收反馈后确认了批量质量门的连带拒绝问题，并明确选择「保留可计算结果，伪迹标为警告」。当前科学版本改为 `official-rbp-v4`；实现身份为 `rbp-runtime-v5-channel-quality`，窗口策略为 `rbp-dynamic-window-v3-channel-quality`，Definition 发布为 2.0.4。未覆盖历史记录或降低摘要校验。

新策略仅由独立 RBP 显式请求：逐通道评估质量，有限的幅值超限、疑似削顶和低幅值数据保留测量值与警告；不再用 75% 干净片段比例拒绝 RBP。缺失/非有限样本拒绝该通道整个窗口；零或无效总功率不可用。正常非零 PSD 保持原数值下限，零信号不通过数值下限伪造功率。其他算法仍用严格默认规则。WPF 更新当前版本消费，在各导联图前显示警告窗口数量与最近原因，明确保留值可能含伪迹。

## 自动验证

后端 API 测试均使用临时 `BRAIN_STORAGE_ROOT`，避免读写用户录音数据库。

| 范围 | 结果 |
| --- | --- |
| `test_rbp_channel_quality.py`、`test_rbp_execution_parity.py`、`test_rbp_five_band_contract.py`、`test_rbp_on_demand_capability.py`、`test_on_demand_rbp_api.py`、`test_algorithms_catalog_api.py`、`test_signal_source_contract.py`、`test_spectral_quality.py`、`test_analysis_spectral_contract.py`、`test_tnk_group.py` | 93 passed |
| `test_official_combined_strategies.py`、`test_official_algorithm_runs.py`、`test_official_algorithm_boundary_conditions.py`、`test_rbp_channel_quality.py`，过滤 `-k 'rbp or healthy or artifact or missing or invalid or annotation'` | 25 passed，49 deselected（与上一组有重叠，不累计） |
| `test_scientific_compute_coordinator.py` 单独重跑 | 5 passed |
| WPF `RbpOnDemandDetailTests`、`RbpResultPreviewTests`、`OfficialCombinedStrategyTests`、`OfflineAnalysisSelectionTests`、`RecordingTimeChartModeTests` | 64 passed |
| `openspec validate unify-standalone-rbp-execution --strict`、`git diff --check` | 通过 |

新回归覆盖两个有序通道的成功、部分通道失败、全通道失败、抛出/返回质量门失败，静态/全量/按需结果与证据一致，一次频谱请求，准备完成后的取消，以及公共顺序、重复和维度校验。

新增真实 Welch/服务边界测试覆盖幅值超限、削顶、低幅值、全零与 NaN；健康通道与单独计算数值完全一致。API 集成测试在同一双通道结果中验证坏通道为空、正常通道有值，完整 artifact 预览与按需响应均保留该语义，且 JSON 不含 NaN。WPF 验证警告不清除数值、不污染健康通道质量，并绑定到导联历史提示。

修正了既有桌面测试夹具：重选 RBP descriptor 会把分析模式重置为静态，测试须重新选择动态后才能验证组合策略。更新了现有本地全量缓存提示文字断言。没有为测试放宽生产身份校验。

额外运行 builtin registry 测试曾得到 25 passed、1 failed：`test_capability_report_requires_explicit_combined_evidence` 要求所有算法的 full 状态均为 validated，而当前 TNK 声明为 unvalidated。本次未改 TNK 声明或该全局断言，因此不宣称全仓测试通过。

共享调度测试 `test_interactive_burst_gives_waiting_batch_a_turn` 在与其他回归同跑时出现一次 `batch_yields == 0`：日志中 batch 在交互请求入队前已获执行。未改调度实现；单独重跑该文件 5 项通过，不能据此声称该时序测试已稳定。

构建仍有既有 NU1701 包兼容警告、预览 SDK 提示和一个 xUnit 集合断言警告；后端有 FastAPI/httpx/anyio 弃用警告。

生产文件规模评估：`contracts.py` 394、`algorithm_recording_context.py` 395；RBP frame/runner/window 和新增 annotated primitive 均低于 400 行，`official.py` 公式未修改。既有 `spectral_analysis.py` 562 行和 `recordings.py` 533 行超过软目标：本次只在服务内路由策略、转发参数和封装证据，通道科学逻辑独立放在 scientific primitive。后续按 PSD/STFT 用例、录音目录/信号查询边界评估拆分，不在本轮机械拆文件。修改的既有长测试文件保持原场景分组，新增回归独立成文件。无本轮触及的文件达到 600 行。

## 待人工验收与限制

重启后端并运行本轮 WPF 构建，在独立「相对频段功率 RBP」页面重新计算，验证静态、按需、全量和「按需预览 + 后台全量」。保留相同录音、导联和参数检查原断层区间：伪迹窗口应保留数值及对应通道警告，硬无效窗口保留不可用原因，不能伪造为零。比较健康通道单独选择与多通道选择的同窗数值、质量和输出顺序。

本轮没有进行真实 WPF 界面操作、24 小时记录压力实验或性能对比，不把自动测试作为人工/临床验收。独立 RBP 仍遵循现有整段连续滤波后切窗的科学契约，可能触发整段科学准备；本次未把它改为 TNK 的窗口级预处理，也不承诺首窗延迟降低。该边界若要改变，需单独修订科学契约并验收滤波边界数值差异。

原始录音、数据库结构和历史 artifact 未迁移；不新增旧实现回退或结果转换。变更尚未归档，未提交推送。
