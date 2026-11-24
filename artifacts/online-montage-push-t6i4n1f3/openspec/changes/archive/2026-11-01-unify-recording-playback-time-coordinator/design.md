## Context

See `proposal.md` for the motivation and scope. 当前 Review 使用 `RecordingPlaybackController` 加页面级 16ms `DispatcherTimer`，TNK 工作台使用自己的 33ms `DispatcherTimer` 与 `Stopwatch`，算法目录还保留 1 秒动态预览计时器。`RecordingTimeMapper` 只负责秒与样本边界转换，`DynamicWindowTargetCoordinator` 只负责请求生命周期；两者都不能成为播放时间所有者。

本设计保持后端请求驱动的边界：后端接收录音相对整数 `cursor_sample`，负责科学窗口规划、质量状态和结果发布；桌面端只负责录音会话时间、读取请求和后端结果展示。原始录音、历史 Run、窗口 artifact、单位、通道顺序和科学预处理契约不迁移、不重写。

## Goals / Non-Goals

**Goals:**

- 为一个打开录音会话建立一个唯一的、可注入的播放时间上下文。
- 以 `cursor_sample` 作为权威时间坐标，秒数只作为由采样率推导的显示/传输值。
- 让 Review 波形、TNK 工作台和动态窗口请求消费同一游标与 generation。
- 让播放推进、波形读取、算法请求和 SciChart 更新互不等待。
- 删除当前局部播放时钟和未接通的伪共享目标事件。
- 用测试证明同一会话只有一个时间源、定位后旧结果不能覆盖新结果，并且算法慢时不阻塞波形。

**Non-Goals:**

- 不修改 Python 科学算法、滤波、窗口边界规划、质量门或结果 schema。
- 不把后端改造成实时播放服务，也不把播放状态持久化到 SQLite。
- 不统一不同算法的窗口长度、刷新步长或计算顺序。
- 不优化 SciChart 图表布局或继续处理本轮暂停的 RBP 图表卡顿问题。
- 不保留旧 TNK 播放时钟作为运行时兼容分支；回滚只通过撤销本次变更完成。

## Decisions

### 1. 每个录音工作区一个时间上下文，而不是全局时钟

由每个打开录音的宿主工作区组合根创建 `RecordingPlaybackTimeCoordinator`，并把同一实例传给该宿主内的波形、TNK 面板和算法请求适配器。它不注册为全局单例，避免两个录音互相影响；同一宿主工作区内不得再创建第二实例。当前独立 Review 窗口和独立 TNK 工作台各自拥有一个实例；将来 TNK 挂入 Review 时，TNK 面板改为接收 Review 宿主提供的实例，不再自建。

上下文保存 recording identity、sample rate、duration samples、整数 cursor、play state、playback rate 和 generation。播放位置由单调时钟锚点计算，但发布前转换为合法整数样本边界；`cursor_seconds` 由 `cursor_sample / sample_rate_hz` 得到。`RecordingTimeMapper` 继续作为唯一转换规则，不允许模块自行乘采样率或使用窗口起点修正。

替代方案：使用全局 `PlaybackService`。拒绝，因为多录音窗口会共享错误的游标和生命周期；使用 `DynamicPreviewCursorSeconds` 作为权威值也拒绝，因为它缺少样本身份、generation 和播放控制语义。

### 2. 单一驱动器，订阅者只消费事件

工作区只保留一个播放驱动器。它可以继续使用 WPF Dispatcher 的节拍来触发状态发布，但节拍不再属于 Review View 或 TNK View；真实位置始终由协调器的单调时钟计算。协调器发布不可变 `PlaybackCursorSnapshot`，事件包含 recording identity、generation、cursor sample、sample rate、cursor seconds、playing 和 playback rate。

事件回调只做轻量状态提交并把读取/算法工作交给已有异步 owner；回调不得等待 HTTP、文件读取、滤波、科学计算或 SciChart 更新。每个消费者使用已有的 latest-only/queued 机制合并过期工作，并自行报告 pending、lag 或 failure。

替代方案：每个消费者保留自己的 timer，再通过秒数对齐。拒绝，因为节拍、舍入、暂停和 seek 的竞态会重新产生漂移；使用后台线程为每个算法创建独立 timer 也违反单一时间所有权。

### 3. 以 generation 分离上下文，以 cursor/revision 分离连续播放目标

播放中的普通 tick 不改变 generation；seek、切换 recording、切换科学配置/来源、重新开始或关闭会话时递增 generation。算法请求仍由现有 request/window revision 和完整 scientific identity 去重与拒绝迟到结果。这样不会把每个 16/33ms tick 当成上下文重置，也不会让同一窗口重复计算。

任何异步结果绑定前必须同时匹配 recording identity、generation、目标 cursor/revision、科学配置身份和实际窗口身份。波形路径和算法路径分别拥有自己的取消与缓存，不互相取消或复用错误结果。

替代方案：每个播放 tick 都增加 generation。拒绝，因为它会使可复用的同一科学窗口不断失效并制造请求积压；只比较秒数也拒绝，因为浮点秒无法表达样本边界和回退竞态。

### 4. 请求协调器不承担时间职责

`DynamicWindowTargetCoordinator` 保留为传输/资源层：管理一个 active work、可替换 pending target、顺序目标、取消、缓存和完成发布。它只能接收 `PlaybackCursorSnapshot` 转换出的请求目标，不能调用系统时钟、修改播放位置或创建播放 timer。`ReviewAnalysisCoordinator` 同样只负责结果加载和 stale-result fencing，不改名冒充时间协调器。

`PlaybackTargetPublished` 没有真实订阅者，因此删除该事件和包装类型；动态窗口目标直接进入现有请求协调器。后端仍只接收客户端提交的整数 cursor 并规划窗口。

### 5. 迁移边界

先迁移 Review 的现有播放控制到新上下文，再迁移 TNK 工作台的 `WaveformPosition` 和算法目标提交，最后删除 TNK 本地 `Stopwatch`、TNK 播放 `DispatcherTimer`；算法目录的 1 秒 timer 仅可作为动态预览的唤醒节拍，位置必须来自协调器，不能自行累加或生成第二个播放时钟。动态预览的手动 step、拖动防抖和 120ms 请求合并可以保留，但它们只能产生显式 seek/target 请求，不能推进播放时钟。宿主注入边界必须允许未来把 TNK 面板挂到 Review，而不引入全局时钟或第二个游标。

迁移不增加旧 API、旧兼容字段或双写路径。实现完成后，源码中只能有一个录音会话播放驱动器；历史录音仍通过原有 reader 和后端窗口接口读取。

## Risks / Trade-offs

- [Risk] Review 与 TNK 当前由不同顶层窗口创建，无法天然共享对象 → Mitigation：当前各自按宿主创建一个上下文；TNK 将来挂入 Review 时通过构造注入接收 Review 上下文，禁止跨窗口全局单例。
- [Risk] 单个 Dispatcher 驱动器仍可能受 UI 忙碌影响 → Mitigation：位置由单调时钟计算而不是按 tick 累加；事件只提交轻量状态，读取和算法工作不在回调中同步等待。真实 UI 帧率仍需后续单独测量。
- [Risk] 删除 TNK 本地时钟会改变 TNK 工作台独立播放行为 → Mitigation：先用 focused tests 覆盖播放、暂停、定位、倍速、结束和窗口目标序列，再进行人工验收；失败时回滚整个变更，不保留两套运行路径。
- [Risk] 旧调用方直接读取 `WaveformPosition` 或 `DynamicPreviewCursorSeconds` → Mitigation：迁移所有源码调用方后才删除旧写入路径；保留只读显示投影，禁止旧字段继续成为写入源。
- [Risk] generation 设计被误用于每个 tick → Mitigation：测试明确普通 tick generation 不变，seek/context reset 才递增；请求身份仍包含 cursor 和科学配置。

## Migration Plan

1. 增加时间上下文、不可变游标快照和边界/状态测试，不接入旧页面。
2. 让工作区组合根创建上下文并迁移 Review；删除 Review 自己拥有的播放时间来源，保留波形缓存和 reader 生命周期。
3. 迁移 TNK 工作台与算法目录到同一上下文；将窗口目标直接提交给现有请求协调器。
4. 删除本地 TNK 播放时钟和无人订阅的共享事件；保留动态预览 timer 作为协调器游标的唤醒节拍，不允许它计算时间。
5. 运行聚焦 WPF 测试、OpenSpec strict、Release build 和 `git diff --check`，再做同一录音的 Review+TNK 手动验收。
6. 回滚策略是恢复变更前的源码版本；不在运行时保留旧/新两套协调器或兼容分支。

## Open Questions

无。若 Review 和 TNK 实际属于两个独立录音工作区，需在实现时按“每工作区一个”拆分上下文实例，但不改变唯一游标和禁止第二时钟的契约。
