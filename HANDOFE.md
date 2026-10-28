# Brain Platform 交接文档

更新时间：2026-10-28（Asia/Shanghai）

本文写给没有当前会话上下文的新会话。源码、测试和 OpenSpec 是事实来源；本文件只提供工作背景和接手顺序。自动化测试通过不等于真实 EEG 录音上的人工验收通过。

## 当前任务

当前主线是完善脑电回顾与 TNK 按需分析的流式体验，同时保证波形显示在降采样时不遗漏尖峰等瞬态，并继续维护统一的通道、电极位置和导联关系配置。

### ANT-Amplify 三个时序指标迁移（2026-10-28）

已将 `ANT-Amplify/eeg-process-main` 中的三个指标接入 TNK 内部子指标边界：

- `tnk_crimson_meditation`：Fz、V/µV 转换、250 Hz、2–45 Hz 与 58–62/48–52 Hz 递归滤波、5 秒 TFLite 窗口、输出 index 2、20 点平滑。
- `tnk_zenlite_calmness` 与 `tnk_zenlite_awareness`：Fz、256 Hz、1200 点 TFLite 窗口、输出 index 0/1、10 点平滑。
- `tnk_zenlite_spectral_relaxation`：复用 Zenlite 预处理，保留 FFTPACK 幅值代理、频带边界、`alpha/(beta_low+beta_high)` 映射和 10 点平滑。

新增 `backend/app/algorithms/tnk/legacy_dsp.py`、`legacy_models.py`、`legacy_biofeedback.py`、`legacy_runtime.py` 和两个模型资产。原始输入通过 `load_time_series_source()` 读取，带 recording/montage 身份、样本边界、通道顺序和 V/float64 契约；神经网络依赖是延迟加载，模型缺失或校验失败返回 `TNK_MODEL_UNAVAILABLE`。公共算法目录仍只有 `tnk_biofeedback`，这四个 ID 只出现在组内 dispatch、evidence 和 provenance。

已通过：模型 SHA-256/资产身份与实际 TFLite shape smoke test、重采样跨 chunk 一致性、频谱放松度映射、warm-up/单位失败、内部子模块清单和 typed raw-source provenance 测试；新增迁移测试 8 项，锁定依赖环境下 TNK/信号源 focused tests 为 94 项通过、1 项跳过。完整真实录音 parity、WPF 人工验收和 OpenSpec 5.2/5.4 仍未完成，不能据此声称与 ANT 实时流完全等价。更宽的 catalog 回归仍有两条既有断言与当前工作区不一致：RBP 已是 v3 多通道实现，而测试仍期待 v2；官方 catalog 已含 `tnk_biofeedback`，而测试仍期待只有九个旧算法。这两条没有归因于本次时序迁移。

共享流与调度修复（2026-10-28）：确认旧的 TNK group 对四个 ANT 时序子指标分别重建读取/预处理，且统一 4 秒窗口会让 Crimson 的 5 秒 warm-up 永久不可用。新增 `TnkLegacyStreamBundle` 后，同一 TNK replay session 只读取新追加的 raw Fz 样本；Crimson 和 Zenlite 保留各自 250/256 Hz、滤波和模型契约，Zenlite 平静度、觉察度和频谱放松度共享一条 Zenlite 流及一次模型推理。bundle 随 replay checkpoint 深拷贝，seek/上下文变化会重置；TFLite 推理句柄在 checkpoint 间共享并串行保护，避免复制 native interpreter，滤波/历史/平滑状态仍独立复制。`group.py` 当前约 631 行，已在 `docs/code-size-policy.json` 登记为高风险拆分项，后续应抽离 manifest/输出契约或 legacy stream orchestration。

播放调度的确定性问题是：WPF 每秒提交一个最新目标，但旧目标计算期间不会被取消；后端动态窗口执行器又是单 worker，单帧超过 1 秒就会占住计算槽，造成结果落后、成批到达和忽快忽慢。现在 TNK 播放调用 `LoadLatestAsync(..., cancelActiveOnSupersede: true)`，新目标会在协调器层取消旧观察，后端在 replay checkpoint 检查处释放订阅和计算槽，再从兼容 checkpoint 重放到新目标；手动按需请求仍保留旧目标完成语义。新增协调器取消测试。该修复已通过自动化测试，但真实 WPF/录音上的卡顿和滞后仍未测量，不能据此声称已经完全解决。

WPF 展示入口已补齐：公共算法列表保留一个可选择的 `tnk_biofeedback`（结果类型显示为“TNK 指标组”，类型筛选增加“TNK 指标”）；进入 TNK 详情后新增“ANT 时序指标”页签，展示 Crimson 神经网络冥想度、Zenlite 平静度、Zenlite 觉察度和 Zenlite 频谱放松度。页签只投影后端返回的时间、数值、单位和不可用状态，不在桌面端重算科学指标。相关桌面 focused tests 3 项通过；当前仍未做真实 WPF 人工验收。

按需请求诊断（2026-10-28）：用户日志中只有 1 次 `POST /api/analysis-windows/ensure`，但同一个 TNK `delivery_id` 有约 35 次 `GET /api/analysis-windows/playback/.../events?after_sequence=0`；每个 HTTP 请求在日志中同时出现应用请求日志和 Uvicorn access 日志，因此不是 70 次科学分析。根因是进入按需模式时提前启动事件消费循环，空缓冲仍每 200 ms 轮询。现已改为首次 TNK 播放目标排队时才启动消费循环；游标停住时不再空轮询。播放期间的事件轮询仍保留，OpenSpec 4.3 的真实录音请求负载与延迟测量尚未完成。

本轮工作区的直接改动集中在“信号预览完整性和阶段诊断”：后端原有均匀步长采样字段保持兼容，新增每个显示桶的最小/最大值摘要；桌面端在原始折线后面绘制可选的显示桶范围包络；后端增加按 request_id 关联的可选阶段耗时日志。目标是防止显示抽稀把瞬态完全跳过，并能区分源数据准备、窗口切片、显示投影和总请求耗时。

## 已完成

### 通道配置未保存状态

- 草稿只有在与已保存基线存在差异时才报告 `HasUnsavedChanges`，保存后会推进基线，再次编辑会重新变脏。
- 详情页返回、主导航切换和窗口关闭共用 `ChannelDraftLeaveGuard`。
- 有未保存修改时提供保存、放弃、取消；取消或保存失败会保留草稿并阻止离开。
- 修复了空 REF/GND 位置在编辑时载入默认坐标、导致刚打开就被误判为已修改的问题：比较时将基线空值按默认模板坐标归一化。
- 相关测试已覆盖保存、放弃、取消、保存失败、未修改直接离开，以及字段/电极位置/启用状态恢复到基线。
- OpenSpec 变更 `openspec/changes/track-unsaved-channel-configuration` 的任务已完成并通过严格校验。

### 电极和导联基础能力

- 已接入统一的电极位置解析、ANT 配置关联、手动位置覆盖、默认模板重置、REF/GND 位置以及导联方向展示。
- 标准位置来源应继续以 MNE/后端位置服务和项目统一通道配置为依据，不能在桌面端重新维护一套独立坐标表。
- 已有电极位置和导联方向相关测试；不要因为截图看起来异常就直接修改坐标，先核对模板、坐标系、通道顺序和当前 recording/montage 身份。

### TNK 流式回顾基础

- 已有提交 `8426062 Implement streamed TNK playback and electrode mapping`，后续提交 `1db231a fix: stabilize EEG electrode mapping and TNK playback`。
- 已实现 TNK 中间帧事件、按窗口身份合并、顺序投递、去重、过期会话丢弃、历史追加、滞后显示和回顾播放生命周期解耦的基础结构。
- 当前 OpenSpec `openspec/changes/stream-tnk-review-analysis` 中第 1、2、3 节和 4.4 已完成；4.1、4.2、4.3、4.5、4.6、4.7 仍未完成，因为真实录音配对测量和人工验收尚未完成。
- OpenSpec 现已记录 ANT session stream、子指标 warm-up 和播放中止旧 replay 的语义；`stream-tnk-review-analysis` 的 3.8 与 3A.1–3A.3 已完成。

### 本轮信号预览改动

- `backend/app/api/recording_signal_preview.py` 新增 `extrema_elapsed_s` 和 `channel_extrema` 响应字段。字段使用 µV，时间为秒，范围时间使用每个显示桶的中心时间。
- `backend/app/services/recording_signal_preview.py` 保持原有 `elapsed_s`、`channels`、`display_stride_samples` 不变；当发生降采样时按桶计算每通道最小值和最大值。未降采样时不额外返回包络。
- 后端通过 `BRAIN_REVIEW_PERF_DIAGNOSTICS=1` 开启 request_id 关联的阶段日志，记录源准备、窗口切片/校验、显示投影、通道数、源样本数、显示点数、步长、包络桶数和总耗时。
- `SignalPreviewContracts.cs` 增加对应可选 DTO；`SignalWaveformView.cs` 校验包络维度和有限值，并把包络绘制在原有波形线后面。包络表示显示桶范围，不表示临床事件时间。
- `openspec/changes/stream-tnk-review-analysis/tasks.md` 第 5 节 5.1–5.4 已勾选完成。

## 当前验证状态

已确认：

- 通道配置 focused tests：26 项通过。
- 桌面主项目使用隔离输出目录 `_temp_build` 构建成功，0 错误。
- `openspec validate track-unsaved-channel-configuration --strict` 通过。
- 本轮执行 `git diff --check` 未发现空白错误。

本轮信号预览相关新增测试文件包括：

- `backend/tests/test_recording_signal_preview.py`
- `desktop-client/BrainPlatform.Desktop.Tests/Algorithms/SignalPreviewExtremaProjectionTests.cs`

本次接手后的验证结果：

- `IdleTnkOnDemandSessionDoesNotStartPlaybackEventPolling` 单测通过（1/1）。
- TNK 回放契约、算法列表和 TNK 工作台 focused tests 通过（52/52），使用 `--no-build` 单进程运行，避免当前桌面进程锁定默认输出目录。
- `DynamicWindowTargetCoordinatorTests` 通过（隔离输出重建后 18/18，包含最新播放目标取消旧 active observation 的覆盖）。
- ANT legacy focused tests：chunk invariant/shared raw source/warm-up 相关测试通过；完整 group suite 仍有 `ai_edge_litert` 环境缺失和两条既有 catalog/group 契约断言待隔离，不能归因于本轮调度修复。
- 后端 `test_tnk_playback_event_contract.py` 与 `test_tnk_playback_delivery_api.py` 通过（11/11）。
- `openspec validate stream-tnk-review-analysis --strict` 通过。
- `git diff --check` 通过；仅有 Git 的 LF/CRLF 转换提示，没有空白错误。
- 构建输出仍提示预览版 .NET SDK 及 OpenTK/OpenTK.GLWpfControl/SkiaSharp.Views.WPF 的 NU1701 警告；没有编译错误。

需要后续确认：

- 后端信号预览相关测试和更宽的桌面回归仍需在不受桌面进程锁定的隔离输出目录中补跑；当前已完成的是本次 TNK 空闲轮询修复及相关桌面 focused tests。
- 流式 TNK 的真实录音配对测量、播放卡顿、结果滞后和包络视觉效果仍未完成。
- WPF 桌面人工验收尚未完成：正常播放、延迟分析、前后 seek、暂停/恢复、通道角色或配置变化、取消和投递恢复都需要验证。
- 构建仍可能出现 .NET 预览 SDK 以及 OpenTK/OpenTK.GLWpfControl/SkiaSharp.Views.WPF 的 NU1701 警告；这些不是当前构建错误，但应记录环境版本。

## 工作区状态和注意事项

当前工作区存在未提交改动，涉及后端信号预览、桌面波形控件、算法客户端测试和 `stream-tnk-review-analysis` OpenSpec。还存在 `_temp_build/`、`desktop-client/BrainPlatform.Desktop.Tests/TestResults/` 等生成物。不要执行 `git reset --hard`、整目录清理或覆盖其他改动；先用 `git status --short` 和 `git diff --name-only` 识别本会话范围。

`HANDOFE.md` 本身原先在工作区处于删除状态，本次已恢复为交接文档。不要把恢复该文档误认为业务代码回退。

## 下一步计划

1. 先查看 `git status --short`、`git log -1 --oneline --decorate` 和本文件，确认接手时的真实状态。
2. 单进程运行信号预览相关后端测试、桌面 `SignalPreviewExtremaProjectionTests` 和通道配置 focused tests；不要并行启动多个 WPF 测试进程，避免生成文件锁冲突。
3. 用同一份本地 EEG 录音、相同通道/导联配置和播放速度，分别测量仅波形与波形加 TNK 的游标 tick、请求耗时、算法滞后、队列/历史覆盖率和图表更新时间；重点确认 `latest_cancel_active` 后旧 server window 是否在 checkpoint 处结束，以及新 replay 是否补齐中间 canonical frame。
4. 开启 `BRAIN_REVIEW_PERF_DIAGNOSTICS=1` 后核对 `signal_preview_perf` 日志；把源准备、切片、科学计算、HTTP/轮询、Dispatcher、集合通知和 SciChart 绘制分开判断，不能把“关闭后端后不卡”直接等同于算法公式慢。
5. 在桌面上验证包络：尖峰落在同一显示桶内时，最小/最大范围应覆盖真实折线；放大到不降采样时不应出现额外包络；无效时间、通道键或范围数据应拒绝渲染。
6. 完成真实录音和人工验收后，再决定是否勾选 `stream-tnk-review-analysis` 第 4 节剩余任务。没有实测数据前，不要声称“卡顿已解决”或“科学结果等价”。
7. 最后运行隔离构建、相关测试、OpenSpec 严格校验和 `git diff --check`，并更新本文件的日期与验证结论。

## 经验和工程约束

- 先区分事实、推测和用户观感。截图只能说明显示结果，不能单独证明 MNE 坐标来源、通道丢失原因或算法性能根因。
- EEG 内部连续信号约定为 `(samples, channels)`、`float64`、伏特 V；信号预览 API 输出 µV；时间使用秒；通道名和数据列顺序必须始终一致。
- 降采样不能简单隔点丢弃高频瞬态。应保留每个显示桶的最小值和最大值，并把它们作为显示范围，不把包络误当成事件检测或临床标注。
- 晚到 TNK 结果必须使用自己的分析窗口时间，不能按当前游标时间追加；无效结果使用结构化不可用/缺口状态，不能填零或静默复用旧值。
- 科学计算、滤波和质量判定归后端；WPF 负责交互、展示和时间轴投影，不能在桌面端复制科学公式。
- 显示缓存、环形缓冲、原始数据持久化和算法状态不是同一个概念，修改其中一个前先确认生命周期和身份键。
- 业务文件以 400 行为软上限；超过 400 行要评估按职责拆分，超过 600 行进入高风险评审，并维护 `docs/code-size-policy.json`。
- OpenSpec 和当前源码测试证据优先于旧聊天结论。任何“已修复”都要说明是自动化验证、源码确认还是真实设备/录音人工验收。
- 不要为了修复一个页面现象另造一套电极或导联数据源；通道配置、标准位置解析、导联引用和 recording/montage 身份必须保持统一。

## 重要路径

- 信号预览后端：`backend/app/api/recording_signal_preview.py`、`backend/app/services/recording_signal_preview.py`
- 信号预览测试：`backend/tests/test_recording_signal_preview.py`
- 桌面波形契约和显示：`desktop-client/BrainPlatform.Desktop/Shared/Waveform/SignalPreviewContracts.cs`、`SignalWaveformView.cs`、`SignalPreviewExtremaProjection.cs`
- 桌面波形测试：`desktop-client/BrainPlatform.Desktop.Tests/Algorithms/SignalPreviewExtremaProjectionTests.cs`
- 通道配置：`desktop-client/BrainPlatform.Desktop/Modules/Channels/`
- 通道配置未保存状态 OpenSpec：`openspec/changes/track-unsaved-channel-configuration/`
- TNK 流式回顾 OpenSpec：`openspec/changes/stream-tnk-review-analysis/`
- 文件规模登记：`docs/code-size-policy.json`
- 隔离构建输出：`_temp_build/`
