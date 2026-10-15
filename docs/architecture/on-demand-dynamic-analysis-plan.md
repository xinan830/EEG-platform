# 按需动态算法分析实施索引

状态更新：2026-10-13。P0/P1 已完成自动化与资源验证，PSD 的 Review/详情接入已实现。
真实文件 114 窗口等价、小时资源、backend 604 项和 WPF 611 项通过；P3 GUI 待验收，
P4 其他 8 种算法未迁移，生产默认关闭。正式行为与阶段编号以
`openspec/changes/on-demand-dynamic-analysis/` 为准；当前证据见
[验证报告](../validation/2026-10-13-on-demand-psd-validation-report.md)。

## 1. 目标

当前官方动态算法通常先完成所选分析范围内的全部窗口，再由 WPF 展示已保存
的时间序列。长记录因此要等整个任务结束，用户无法先看到当前播放位置的窗口。

本计划希望提供“按需动态分析”：用户播放或跳转到时间点时，后端只确保当前
位置对应的动态窗口结果存在；返回后立刻展示。向前播放可有界预取邻近窗口，
回看已计算窗口命中缓存。它必须复用统一调度和记录/配置/版本身份，不在各算法
页面各自堆一套轮询、缓存和取消逻辑。

目标体验（窗口 5 秒、步长 1 秒）：游标到 5 秒时请求首个合规窗口；游标到 6
秒时请求 1–6 秒窗口；跳到 100 秒时先准备 95–100 秒对应窗口，不要求先计算
0–95 秒的算法结果。实际窗口边界、窗口索引、状态和质量证据始终以后台返回值为准。

## 2. 现状证据与必须区分的耗时

- `backend/app/services/run_queue.py` 当前通过统一 Executor 执行完整 Run，完成后
  才把结果和 artifact 交给 Repository；WPF 的 `AlgorithmRunCoordinator` 轮询到
  terminal 状态后才加载完整结构化预览。
- `backend/app/services/results.py::window_preview` 是完成结果的只读窗口读取接口，
  不会计算新窗口。
- 当前运行时动态算法通过 `execute_dynamic` 生成一个窗口序列；PSD 的循环在
  `backend/app/algorithms/psd/runner.py`。不同算法实际数学定义和最小窗口约束不同，
  不能假设窗口函数完全相同。
- PSD 调用 `SpectralAnalysisService._preprocessed_recording`，目前会对整条记录做
  连续零相位预处理并缓存；FAA 的科学契约也声明先连续预处理再分 Epoch。因此，
  把算法输出改成单窗口请求，并不自动消除首次整段预处理等待。
- `preprocess_blocks` 虽然按块处理，仍是整段零相位滤波的实现；不能把“分块执行”误称
  为“只读取当前播放位置附近的数据”。

性能验收必须分别量化：记录注册/读取、连续预处理、首窗口科学计算、窗口传输/绑定、
后续窗口计算。功能目标是无需等完整窗口序列；是否显著降低首窗口墙钟延迟是需实测的
性能假设，而不是计划预先承诺的结果。

### 2.1 初步本地基线（2026-10-12）

使用本机一条约 60.6 秒的已完成 WPF 记录，以 acquisition montage 的 `F7-REF`
运行动态 PSD（5 秒窗口、1 秒步进、57 个窗口）。在隔离临时 SQLite/artifact 目录中
运行，源记录只读；测试结束后状态为 completed，单一 NPZ artifact 在完整计算后出现。

| 阶段 | 实测 | 解释 |
| --- | ---: | --- |
| WPF 记录注册 | 83.3 ms | 包含 manifest/hash 检查与分析记录登记 |
| 冷连续预处理 | 569.7 ms | 整段源读取及连续零相位预处理，不是单窗成本 |
| 首窗总耗时 | 572.6 ms | 包含上面的冷预处理 |
| 首窗预处理之后的 PSD 计算 | 2.9 ms | 首窗总耗时减去嵌套预处理实测值 |
| 后续窗口计算中位数 | 2.3 ms | 同进程已命中整段预处理缓存 |
| 57 窗口完整 Run | 1468.7 ms | 含执行框架、进度持久化和最终 artifact 写入 |

此小样本能证明“结果发布发生在全部窗口执行之后”，并显示冷首窗的主要已测成本是
连续预处理；它不能代表长记录/小时级性能，也不能将完整 Run 其余约 0.9 秒简单归因于
算法计算（其中还含执行框架、SQLite 进度更新和 artifact 写入）。下一阶段的基线必须
分别标记加载、预处理、窗口算法、持久化/传输，并在更长记录上重复冷/热测试。首阶段
因此承诺“提前发布访问到的窗口”，不承诺在未完成新的预处理科学契约前必然缩短首窗时间。

## 3. 首版范围

- 仅离线已完成记录；沿用 Review 唯一播放时钟和 int64 记录相对样本坐标。
- 先做一个算法垂直切片（建议 PSD），同时设计通用的动态窗口能力契约；逐算法迁移
  作为按能力清单展开的后续阶段，不能以 PSD 通过宣称所有算法已完成。
- 覆盖回溯游标跟随和算法详情页动态游标的按需窗口请求。两处调用相同的后端窗口
  服务、窗口身份和缓存语义；各自页面只拥有展示和生命周期组合。
- 保留现有完整动态 Run、历史 Run、artifact、export、`structured-preview`、
  `window-preview` 和静态算法行为。新增按需入口不能改变旧记录的解释或覆盖历史结果。
- 任何源数据、配置、参考映射、算法科学版本、实现版本或窗口策略改变都会形成新的
  计算身份；结果不可跨身份混用。

## 4. 非目标

- 不在 WPF 重新实现滤波、Welch、STFT、频段积分、比值或质量门。
- 首版不把离线零相位结果称为实时/因果结果，也不静默切换到单窗口滤波或因果滤波。
- 不改原始记录、不回写采集快照、不自动生成未被算法政策允许的 Partial 窗口。
- 不移除旧 Run API，不批量迁移既有 artifact，不把小时级全矩阵装入 UI 内存。
- 不做设备实时采集算法、不新增多个页面私有后台 worker、不允许无界预取或并发。

## 5. 科学契约与关键决策门

### 5.1 保持数值契约

第一阶段按需窗口必须与同配置、同源记录的既有完整动态 Run 对应窗口数值一致，
包括窗口起止样本、单位、quality/state、失败原因和 provenance。请求窗口不能通过
缩短/改变预处理区间来取巧；原始数据仍只读。

由于当前部分算法依赖整段连续零相位预处理，第一版允许“整段预处理准备好后，按需
计算窗口指标”。这能省去未访问窗口的算法计算、结果矩阵生成和传输，但冷启动仍可能
等待全记录预处理。若基准显示该等待仍不可接受，不允许在本变更中偷偷采用另一滤波
方案；应另立科学变更，明确因果滤波/边界策略、版本化、独立数值对照及影响范围。

### 5.2 用户可见状态

至少区分 `preparing_source`、`queued`、`computing_window`、`ready`、`rejected`、
`unavailable`、`cancelled`、`failed`。窗口计算中保持上一完整图表帧，但标明它属于
哪个已完成窗口；不得把旧窗口伪装成当前目标。数据不足、gap、质量拒绝和系统失败
使用不同状态及结构化原因，缺失数值为 `null` 而不是 0。

### 5.3 时间及窗口

- `RecordingRelativeSampleIndex`（int64）是权威坐标，窗口采用半开区间
  `[start_sample, end_sample)`；秒坐标仅用于显示/兼容，并回显真实范围。
- 后端的算法策略负责窗口构造、对齐、最小窗口、预热及尾窗政策。前端不生成窗口。
- 同一配置/源上的窗口键需包含算法 ID、科学/实现版本、源 hash、完整规范化配置
  （含有序通道、参考和预处理）、窗口策略版本以及 start/end sample。
- 请求游标未达到所需完整窗口时返回 `insufficient_data`，不能选择未来窗口。
  gap 上下游不拼接信号来满足窗口长度；gap 策略按算法契约明确返回 unavailable/reason。

## 6. 建议架构

1. **窗口能力接口**：扩展算法模块契约，让支持按需执行的算法提供后端定义的窗口计划、
   单窗口执行入口以及准备/预处理身份。通用服务编排，算法模块保留公式、质量策略和
   特殊预处理语义。无能力声明的模块继续走旧完整 Run，不走隐式 fallback。
2. **按需窗口协调服务**：接收 recording、algorithm、config 和目标 sample；解析/校验
   输入，查精确身份缓存，去重相同窗口的 in-flight 工作，排队执行单窗口并返回任务/窗口
   状态。目标跳转时可取消只属于旧目标的等待者；共享中的计算只有无人订阅且能安全取消
   时才取消，不能让一个页面取消另一个消费者正在使用的结果。
3. **可追溯持久化**：窗口结果不可变、按窗口独立写入并原子发布，包含源身份、Run/会话
   身份、窗口 sample/time、结果轴/数组单位、质量证据和版本。不能在同一个已完成 Run
   上追加或篡改数据，不能让未完整写入结果被读取。需评估新增 session/Run 关系、artifact
   索引、SQLite migration、崩溃恢复和 retention 后再定具体表结构。
4. **有限缓存与资源预算**：区分科学结果缓存（跨播放/窗口复用且严格按身份）和 WPF
   图表帧缓存（小 LRU、仅展示）。预取默认关闭或仅前方一个窗口，必须有可配置的队列、
   CPU/内存/磁盘上限、前台优先级、LRU/保留策略和缓存命中/耗时诊断；最终数字通过压测
   选定。
5. **前端 latest-only**：播放高频游标更新只转为接受的窗口目标；相同窗口命中当前/缓存
   不重复发请求；更换窗口取消旧请求等待并令旧响应失效。只有 recording、config revision、
   source identity、target revision 全部匹配才原子绑定新结果。算法 API/Contracts 提供统一
   类型；PSD 模块解析及 SciChart 保持独立，Review 不认识 PSD 数组结构。
6. **诊断**：记录 session/request/run/window ID、目标/实际 sample 范围、source/config
   fingerprint 摘要、缓存命中、排队/预处理/单窗口耗时和取消原因；不写 EEG 数组、姓名或
   本机绝对路径。可复制诊断快照帮助定位窗口问题。

## 7. 接口候选与兼容性

当前实现提供 `POST /api/analysis-windows/ensure`、窗口状态/结果读取和订阅取消接口；
202/200 语义、状态 schema 和错误码以正式 OpenSpec 为准。生产默认通过
`BRAIN_ENABLE_ON_DEMAND_PSD=0` 关闭，科学验证完成后才可打开。

截至 2026-10-13，WPF 已增加类型化按需窗口客户端、PSD provider 适配、共享
latest-only binding 和页面订阅释放；通过 `BRAIN_WPF_ON_DEMAND_PSD=1` 才会
选择该入口，默认仍走完整 Run。后端窗口服务增加了有限 pending 容量、最后订阅者
取消、lease 状态检查、原子 NPZ/JSON 产物校验、孤儿临时文件清理和结构化耗时日志。
这些实现不等于生产门禁已打开；真实连续 source 的数值证据和小时级服务链路压测
已保存，端到端 WPF GUI 仍是 P3 退出条件。原语级早期基线见
`docs/validation/2026-10-13-on-demand-dynamic-analysis-baseline.md`；后续实测以当前验证报告为准。
请求包含 recording ID、algorithm ID/scientific version、规范配置和 cursor sample；不接收
客户端提供的科学窗口边界作为权威值。响应/轮询返回目标窗口计划和状态，ready 后返回
单窗口科学结果与 provenance，不返回 EEG 原始数组。

当前已决定使用独立 session/window/attempt/subscription 表和不可变窗口产物，
不往旧 completed Run/result_summary/artifact 追加。旧 Run 与既有接口始终可读；
新增能力关闭时使用旧完整 Run 功能，而不是错误地显示空结果。

## 8. 阶段与退出条件

| 阶段 | 工作 | 进入下一阶段的证据 |
| --- | --- | --- |
| P0 契约 | 能力、身份、API/state/schema、订阅/权限、资源和保留决策 | 已冻结并 strict/test 通过 |
| P1 后端与 PSD 科学门 | 单窗/原子产物、取消去重、OS 恢复、旧 API 与数值/资源验证 | 已通过真实源及全量自动化；默认关闭生产开关 |
| P2 WPF 垂直切片 | Review/详情共享 coordinator，latest-only，独立 PSD presenter | 实现与自动化已通过；GUI 不由 VM 测试替代 |
| P3 真实验收/性能 | 实测源/小时负载、请求/资源、GUI 播放跳转和不可用时独立波形 | 数据与工程检查通过，GUI 待验收 |
| P4 逐算法迁移 | 每模块 capability、科学等价与界面证据；FAA 单独契约 | 用户确认 PSD 自动化通过后继续迁移，GUI 留到最后统一验收；开关默认关闭 |

## 9. 验收矩阵

- **延迟**：同一数据/机器比较完整 Run 和按需模式的注册、source preparation、首窗、
  第 N 窗、全量时间；报告 p50/p95/峰值内存/磁盘。不得只报算法窗口时间而隐藏预处理。
- **科学一致性**：所有已接入算法，用固定 fixture 比对完整动态 Run 与窗口请求同窗口结果；
  验证单位、数组 shape、频率/时间轴、质量、结构化失败原因、sample range 和版本身份。
- **时序**：游标 0、最小窗前、边界前后一个样本、尾端、越界；非零首计数器、NaN/gap、
  暂停区间、窗口跨 gap。没有未来窗、无窗口伪造、无跨 gap 拼接。
- **竞态**：快速拖动 A→B→C 后 A 晚返回；切记录、通道、参数、科学版本；关闭/重开面板；
  只有当前身份的 C 可绑定。相同 in-flight 请求去重，多消费者取消语义正确。
- **资源**：有限队列、并发、预取、memory/disk LRU，缓存失效/损坏/部分写入/重启恢复；
  持续游标变化不发无界请求。
- **回归**：旧 CreateRun/EnsureRun、历史结构化预览、导出、已完成窗口读取、取消和静态
  算法不回归；raw recording hash 不变；WPF 无科学计算。
- **可观测性**：UI 可读质量原因；诊断信息可关联后端日志；日志不泄漏 EEG 数组、个人身份
  或完整本机路径。

## 10. 风险与回退

- **最大风险：首结果并不会自动变快。** 整段零相位预处理若占据主要时长，窗口按需只减少
  后续未请求的算法工作。P0/P4 必须测量，不以架构描述替代实测。
- **科学风险**：每窗口独立 zero-phase 可能产生边界差异；因果滤波则产生不同相位/幅值与
  延迟。两者都需要独立版本和科学验证，不能作为性能开关的隐藏实现细节。
- **持久化风险**：进程中断时窗口记录/artifact 不一致；采用临时写入+校验+原子发布，
  后台恢复未完成任务，损坏结果必须明确 unavailable 并可重建。
- **资源风险**：跳转触发并发与长记录派生缓存可能耗尽 worker、内存或磁盘；前台优先，
  排队与缓存必须有硬上限，拒绝/排队原因对用户可见。
- **回退**：保留完整动态 Run 路径和旧 API；按需入口可用功能开关关闭。可重建准备缓存
  与临时文件可清理，但已发布 READY 窗口不做自动 LRU 删除；不触碰源录制或旧 artifact。

## 11. 预计接入面（草案，实施时以测试驱动更新）

- Backend：algorithm module capability/contracts、窗口调度/持久化 service、Run/Artifact
  repository schema、API router/serialization、后台 worker 与日志。
- Algorithm modules：各动态 runner 增加单窗口执行适配；预处理共享只在科学语义相同的
  能力间共享；每模块保留独立算法结果结构和 quality policy。
- WPF：Algorithms API/Contracts、共享动态请求协调器、Review playback target adapter、
  Algorithm detail cursor adapter、PSD presenter/preview state；不在通用 Catalog 写 PSD 分支。
- Validation/docs：后端 API/持久化/算法对照测试、WPF 请求竞态/绑定测试、长记录性能基线，
  OpenSpec 与架构/验收报告。

## 12. P0 实施前核对表（基于当前源码，2026-10-12）

本节不是新接口实现，也不替代正式 OpenSpec；它把进入 P0 时不能再次猜测的
现状固定下来。

### 已存在、必须保持兼容的能力

- `backend/app/algorithm_runtime/contracts.py` 已定义动态窗口策略、窗口状态和
  `execute_dynamic` 能力边界。没有声明新能力的算法继续走完整 Run。
- `backend/app/algorithm_runtime/windows.py` 已负责动态窗口构造；窗口起止、最小
  窗口和 Partial/Complete 规则不能在新协调器中复制。
- `backend/app/services/artifacts.py` 已有版本化窗口索引与单窗读取，但它读取的是
  已完成 artifact；不能把它误称为运行中窗口发布，也不能让新 API 绕过完整性校验。
- `backend/app/api/results.py` 的 `window-preview` 仍是已完成 Run 的只读接口；P0
  必须保留其响应和错误语义，不把新 ensure 请求伪装成该接口。
- WPF 的 `ReviewAnalysisCoordinator`、`ReviewAnalysisBinding` 和
  `PsdReviewAnalysisProvider` 已统一处理 PSD 的 Run 复用、游标 latest-only 和结果绑定。
  新按需入口应接入 provider/capability 边界，不能在 `RecordingReviewViewModel` 中
  增加第二套 PSD 轮询或算法分支。

### P0 必须冻结的决策

1. **窗口身份**：使用后端生成的 `window_id`/`window_index` 与
   `[start_sample, end_sample)`；客户端只提交 recording、algorithm、规范化配置和
   cursor sample，不提交权威窗口边界。
2. **状态**：至少区分 `preparing_source`、`queued`、`computing_window`、`ready`、
   `rejected`、`unavailable`、`cancelled`、`failed`；旧完整 Run 的
   `queued/running/completed/gate_failed` 不改名。
3. **发布**：窗口结果必须临时写入、校验后原子发布；未发布结果不能被窗口读取接口
   看到。一个按需窗口完成不能追加或修改既有 completed Run/artifact。
4. **取消与去重**：同一窗口身份的 in-flight 工作共享；取消一个订阅者不能取消仍有
   其他订阅者的工作。配置失败后的重试必须使用新的请求身份。
5. **科学语义**：PSD 首片沿用连续零相位预处理身份；不得为降低首窗延迟改成窗口独立
   滤波。若未来改为因果/状态传递，必须另立科学 OpenSpec。
6. **兼容回退**：能力未声明、源快照无法验证、artifact/index 损坏或资源超限时，明确
   返回不可用/失败原因，不静默回退到 raw 输入、不伪造数值。

### 进入 P1 前必须有的证据

- PSD 单窗口与现有完整动态 Run 对同一窗口的数组、单位、范围、质量和 provenance
  逐项对照，并报告容差。
- 相同窗口并发请求只产生一个计算；不同配置、不同 source hash 或不同科学版本不能
  命中同一结果。
- 进程中断、部分文件、损坏索引和取消后的重试均可恢复或明确失败，不能留下可绑定的
  半成品。
- 现有旧 Run API、`window-preview`、导出和历史 artifact 测试全部保持通过。

本节保留实施前核对依据；当前实现状态由开头索引与 OpenSpec tasks 定义，
任何阶段都不授权按需路径降低现有科学结果标准。
