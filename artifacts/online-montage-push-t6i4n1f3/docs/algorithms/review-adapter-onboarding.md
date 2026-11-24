# 算法回溯适配器接入清单

更新日期：2026-11-09。算法列表/详情中的 Run 接入不自动意味着记录回溯（Review）已接入。当前后端目录和算法工作区均注册 11 个公共 ID，Review 注册其中 9 个，未注册 `band_ratio` 与 `peak_frequency`。本页的“已注册”只说明源码接入，不表示科学等价、真实录音或人工 UI 验收通过。

## 固定边界

以下为接入与验收要求；当前“已注册”不证明这些要求均已完成，缺少证据的路径仍按未验收处理。

- `ReviewAnalysisCoordinator` 管游标目标、播放/拖拽触发、取消订阅和过期结果隔离；不含算法 ID 分支、科学公式或输出数组语义。
- 每个算法 adapter 负责 immutable config/snapshot 到请求的映射、该算法响应身份校验和 typed 输出投影；presenter 负责单位、轴、状态/质量与证据的呈现。
- 后端模块拥有窗口规划、连续预处理、缺样处理、公式、质量门和真实范围/provenance。桌面端不从全量序列伪造按需窗口、不重算、不为不支持能力回退到逐游标 Run。
- 只有后端窗口实现、adapter、presenter、科学等价证据和桌面行为验收全部通过，才可注册。目录 `is_runnable` 或后端 `execute_window` 存在都不足以开放回溯。
- 按需窗口 API 返回规范 `scientific_configuration_json` 及其 `configuration_sha256`。桌面端按原始 UTF-8 JSON 字节校验摘要，再验证请求配置与后端规范化配置一致后绑定快照；桌面端不得自行重建科学上下文或摘要。

## 官方目录项与客户端注册状态

下表按公共算法 ID 计数：10 个独立算法加 1 个 TNK 指标组。两个 Brainbeat ID 共用 `brainbeat/` 包；TNK 内部子指标不计入公共目录。三列分别核对以下源码，互不替代：

- 后端目录：[builtin_modules.py](../../backend/app/algorithms/builtin_modules.py)，TNK 公共组适配器由 [runtime.py](../../backend/app/algorithms/tnk/runtime.py) 构造。
- 算法工作区：[AlgorithmUiRegistry.cs](../../desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/AlgorithmUiRegistry.cs) 和 [AlgorithmRunResultHandlerRegistry.cs](../../desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Shared/AlgorithmRunResultHandlerRegistry.cs)。
- Review：[ReviewAlgorithmPanels.cs](../../desktop-client/BrainPlatform.Desktop/Modules/Review/Analysis/ReviewAlgorithmPanels.cs)；标量 ID 来自 [RecordingReviewViewModel.cs](../../desktop-client/BrainPlatform.Desktop/Modules/Review/ViewModels/RecordingReviewViewModel.cs) 的参数对象。

| 公共算法 ID | 后端 Runtime/目录 | 算法工作区详情与结果处理器 | Review 面板 |
| --- | --- | --- | --- |
| `psd` | 已注册 | 已注册 | 已注册 |
| `stft` | 已注册 | 已注册 | 已注册 |
| `rbp` | 已注册 | 已注册 | 已注册 |
| `band_ratio` | 已注册 | 已注册 | 未注册 |
| `peak_frequency` | 已注册 | 已注册 | 未注册 |
| `iapf` | 已注册 | 已注册 | 已注册 |
| `theta_beta` | 已注册 | 已注册 | 已注册 |
| `faa` | 已注册 | 已注册 | 已注册 |
| `brainbeat` | 已注册 | 已注册 | 已注册 |
| `brainbeat_individualized` | 已注册 | 已注册 | 已注册 |
| `tnk_biofeedback` | 已注册 | 已注册 | 已注册 |

输入、参数、输出单位和窗口策略以各模块当前 manifest/config/window 为准，执行归属见[执行清单](official-algorithm-execution-inventory.md)。策略验证见[管理基线](official-algorithm-management.md)，带日期的自动化和人工证据见[验证索引](../validation/README.md)。特别是 TNK 的 full/on-demand/combined 仍为 implemented / unvalidated，不能因三处均已注册而提升为已验收。各模块专属 quality rule 和 evidence 仍由各自 adapter/presenter 解释。

## 每个 adapter 的接入验收

1. 从当前 manifest/config/window 固定完整配置字段、角色次序、策略版本、输入表示、连续预处理范围、缺样行为及允许窗口范围；禁止根据 UI 显示通道名猜测信号角色。
2. 实现 adapter：冻结 recording ID/source checksum、目标整数 sample、目录定义/科学/实现身份及完整模块配置；把 API 返回身份与窗口实际范围逐项校验。未知或缺失身份按失败处理。
3. 实现 typed presenter：核对轴、单位、数组形状、窗口起止、质量/状态、失败原因和算法专属 evidence；状态不是有效数值的响应不得渲染成成功值。
4. 使用独立窗口参考或既有冻结数值验证至少首窗、中间窗、尾窗、缺样/拒绝状态和切换目标；与 Run/全量序列比较时仅比较同一科学样本范围、同配置和相同算法版本。
5. 自动化覆盖播放每秒观察、拖拽 release、同窗去重、反向 seek、快速替换、关闭/切换记录、取消和异常；手动 WPF 验收需实际检查结果范围与 provenance。
6. 验收通过后才加入显式 registry，并同步本表、架构文档、OpenSpec tasks 和验证证据。未完成保持 unavailable；不使用全量结果临时适配，也不添加通用 JSON fallback。
