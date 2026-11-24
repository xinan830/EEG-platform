# 按需动态分析实施任务

状态校正：2026-10-15。勾选表示该条交付范围已完成并有证据，不代表整期已验收。
未勾选项后的“已实现/待验证”说明用于区分实际缺口；后端成功不替代 WPF GUI 验收。

后续状态：用户完成九个算法及综合场景验收，并要求无需命令直接选择模式。
`enable-accepted-offline-analysis-selection` 将已验收模块默认开放；下文“默认关闭”保留各阶段当时状态，不再代表当前默认值。

## 当前分阶段交付与验收

2026-10-13 用户最新要求覆盖此前“全部完成后统一 GUI 验收”的工作顺序：
每阶段完成实现、自动化验证及 tasks 同步后，停下来等待用户验收；确认后再进入下一阶段。
生产开关仍默认关闭，自动化验证不代表 GUI 已验收。阶段耗时只能估计，不承诺固定结束时间。

### 阶段 1：四个标量算法 WPF 接入与验证（已验收）

- [x] BandRatio / PeakFrequency / IAPF / ThetaBeta 后端单窗及真实格式 API 对照：专项 30 passed。
- [x] 完成四模块独立窗口投影、详情页绑定、session 隔离和 default-off handler；四个 handler 均保持生产默认关闭。
- [x] 补 WPF 专项：Partial 有值、失败定量/null、通道与配置切换、关闭重开、同窗去重、有界历史及旧完整 Run 回退；标量专项 25/25，RBP 回归 5/5（合计本次筛选 30/30）。
- [x] WPF 全量测试 641/641、Release build 0 errors、OpenSpec strict、文件规模、diff 检查通过；Release 保留 6 个既有 NU1701 警告，未见新增错误。
- [x] 用户阶段 1 验收通过；2026-10-13 用户回复“继续验收完毕”。此确认仅覆盖阶段 1，不代表 PSD Review 或其余算法 GUI 已验收。

### 阶段 2：迁移证据与文档同步（已验收）

- [x] 汇总 RBP 与四标量模块的误差、范围、单位、质量和 evidence 对照报告，更新能力矩阵与接入说明；报告见 `docs/validation/2026-10-13-on-demand-metric-migration-stage2.md`。
- [x] 用户阶段 2 验收通过；用户回复“继续验收完毕”（2026-10-13）。此确认仅覆盖阶段 2，不代表 FAA / Brainbeat / STFT GUI 已验收。

### 阶段 3：FAA

- [x] 独立完成成对源、连续预处理、epoch/最小干净 epoch 的后端与 WPF 迁移、测试和报告；报告见 `docs/validation/2026-10-13-on-demand-faa-stage3.md`，生产开关保持关闭。
- [x] 用户阶段 3 GUI 验收通过；生产开关仍保持关闭，确认不代表其他算法 GUI 已验收。

### 阶段 4：Brainbeat

- [x] 独立完成双通道、IAPF 派生依赖、无跨窗 EMA 的后端与 WPF 迁移；真实 Recording/API 逐窗对照、质量/null、取消、schema、registry 和 WPF provenance 绑定测试通过；报告见 `docs/validation/2026-10-14-on-demand-brainbeat-stage4.md`；两个生产开关保持关闭。
- [x] 用户阶段 4 GUI 验收通过；用户随后确认继续推进（2026-10-14），生产开关仍保持关闭。

### 阶段 5：STFT

- [x] 独立完成时频坐标、外层范围、矩阵 shape/质量的后端与 WPF 迁移；按需结果复用连续预处理 spectrogram loader，首个最小窗口只发布真实时频行，ready/rejected/insufficient 状态和 provenance 已覆盖。
- [x] STFT 后端专项 `7 passed`，registry/output-schema 回归合计 `14 passed`；backend 全量 `657 passed`。
- [x] STFT WPF 专项 `3 passed`，WPF 全量基线 `651 passed`；后续自动播放回归新增 1 项，当前全量 `652 passed`；生产开关 `BRAIN_ENABLE_ON_DEMAND_STFT` 与 `BRAIN_WPF_ON_DEMAND_STFT` 默认关闭。
- [x] 自动化阶段报告见 `docs/validation/2026-10-14-on-demand-stft-stage5.md`；用户阶段 5 GUI 确认见下一项。
- [x] 用户阶段 5 GUI 验收通过；用户确认“可以了”（2026-10-14）。生产开关仍保持关闭。

### 阶段 6：最终收口

- [x] Backend/WPF 全量回归、Release build、strict/规模/diff 检查及最终报告通过；证据见 `docs/validation/2026-10-14-on-demand-dynamic-analysis-final.md`。
- [x] 用户完成 PSD、STFT、RBP、频段功率比、频段峰频率、IAPF、Theta/Beta、FAA、Brainbeat 的全量/按需正常路径复验；根据逐项反馈登记，未新增代理 GUI、数值或性能证据。见 `docs/validation/2026-10-15-offline-modes-wpf-acceptance.md`。
- [x] 用户在综合场景验收指引后反馈“可以正常”，登记 Review 生命周期与后端停止/恢复综合验收通过；证据为用户反馈，无新增代理 GUI 或性能测量，见 2026-10-15 人工验收记录。

## P0：契约、身份和持久化设计（实现前决策门）

- [x] 清点当前所有动态 algorithm module 的窗口构造、最小窗口、gap/Partial/尾窗、质量规则、输入来源和 preprocessing scope，产出能力矩阵；不得假定所有 runner 可单窗调用。矩阵见 `docs/algorithms/on-demand-dynamic-capability-matrix.md`；P0 首期仅 PSD 声明能力，后续九个算法已按 P4 分别完成适配与验证。
- [x] 冻结算法 `DynamicWindowCapability` protocol：policy/plan、prepared-source 依赖身份、单窗执行、取消检查、输出 schema 和科学证据边界；没有能力的算法继续旧完整 Run。已落在 `backend/app/algorithm_runtime/on_demand.py`，PSD 适配器已实现但仍受科学验证开关保护。
- [x] 冻结 session、window、attempt、subscription 的身份与 SQLite schema/migration；声明配置/源预处理共享关系、项目权限域、唯一索引和并发事务规则。已落在 `on_demand_migrations.py`，并覆盖 partial unique index、外键、半开窗口约束和幂等迁移测试。
- [x] 冻结 ensure/status/result/subscription/cancel HTTP 方法、payload、响应码、稳定错误码、sample 坐标及旧 API 兼容边界。具体路径和错误码已写入 `specs/analysis/on-demand-windows/spec.md`，由 `api/analysis_windows.py` 提供；审计字段和权限矩阵仍待 P1/P2。
- [x] 冻结窗口生命周期和状态迁移：`preparing_source / queued / computing_window / ready / insufficient_data / rejected / unavailable / cancelled / failed`，明确科学拒绝与执行失败区别。已落在 `backend/app/algorithm_runtime/on_demand_lifecycle.py` 并有纯状态机测试。
- [x] 冻结订阅者取消、最后订阅者退出、不可中断工作、用户显式取消权限和新 attempt 重试语义；不得单个 UI 消费者取消共享工作。设计/spec 已规定独立 subscription、项目域隔离和 immutable generation retry；仓储/API 测试已覆盖。
- [x] 冻结原子发布/校验/恢复、临时文件清理、ready 结果损坏、lease/worker 崩溃与历史兼容方案；不得写入/追加已完成 Run artifact。设计/spec 已冻结 token fencing、checksum、原子 READY 关联与 lease 恢复；实际进程终止验证单列于 P1。
- [x] 冻结队列、interactive 前台优先级、并发、预取、CPU/内存/磁盘预算和不可删除结果保留策略；用压测选择数字，不做无界增长。design 已冻结单 worker、无预取、16/8/4 活动任务配额、map/free-disk/artifact 预算；小时级 2 通道实测与资源拒绝测试通过。预算不是所有负载下全进程 RSS 保证，高采样率/多通道边界已披露。
- [x] 记录 PSD 连续 zero-phase 预处理边界及准备缓存身份；明确不得局部截窗滤波或更改 scientific version。整段预处理后取窗；准备键包含 source hash、ordered schema、reference/source mode、filter/notch、import/contract/build，`test_prepared_source_identity.py` 验证隔离。
- [x] 将 P0 API/schema/state/存储决策写入 specs 与 design，增加纯协议与状态转换测试后运行 `openspec validate on-demand-dynamic-analysis --strict`。协议、lifecycle、migration 和资源场景均已测试；2026-10-13 strict valid。

## P1：通用后端执行基础设施

- [x] 增加持久化 migration/repository；已实现 session/window/subscription/attempt 的事务边界、项目隔离、并发幂等、生命周期校验、科学身份去重、订阅隔离、lease token fencing 和过期重试，并有仓储及旧库升级测试；真实 OS 进程死亡/新进程恢复专项通过。
- [x] 增加算法能力注册/验证；`DynamicWindowCapabilityRegistry` 只暴露显式声明且支持 dynamic 的模块，未声明能力返回 `ON_DEMAND_CAPABILITY_UNSUPPORTED`；生产服务默认不启用未通过科学门禁的 PSD。
- [x] 实现 ensure、status、result、subscription/cancel API；类型化输入、稳定错误、checksum 读取、ensure/cancel 审计、显式 project-recording 归属验证已接入。当前为本地平台项目域隔离，不声明多用户认证能力。
- [x] 实现有界 worker/前台优先级/同键去重/多订阅取消/新 attempt 重试/进程恢复。单 worker、事务级全局/项目/recording 容量、去重、订阅隔离、generation retry、后台 lease 恢复和真实 OS 终止测试均通过。首期没有 prefetch，所有任务为前台任务；恢复测试在终止后主动过期临时 DB lease，不声明生产恢复延迟已测。
- [x] 实现输出 schema+unit+provenance 校验、checksum 与 atomic publish；模块声明字段/单位校验、finite/shape、NPZ/JSON flush+fsync、fenced READY 关联已接入；部分发布、损坏、重复/过期 token 发布注入测试通过。
- [x] 首期不启用无测量支持的预取；实现结构化容量限制和可观察指标。入库前事务级活动任务上限、429、准备/计算/排队/完成耗时日志已实现，admission/resource 专项覆盖拒绝不留下任务、READY 保留和资源失败不伪装为质量拒绝；进程级 CPU owner 不增加重复限流器。

## P1：PSD 能力与科学等价门

- [x] 为 PSD 实现窗口能力适配，使用原窗口策略、连续 zero-phase source 与既有质量/输出格式。真实 WPF 格式合成文件经 RecordingService、artifact/service/API 链路逐窗对照通过，生产开关仍关闭。
- [x] 在独立 source fixture 上对照 full dynamic Run 对应的每个 PSD 窗口；声明 rtol/atol 并报告 max abs/relative error、范围、频率轴、单位、质量、evidence 与 provenance。fixture 与真实文件链路 114 窗口通过；max abs/relative error=0，窗口 evidence 相等。报告见 `docs/validation/2026-10-13-on-demand-psd-validation-report.md`。
- [x] 验证首窗前/边界样本/gap/记录尾端/quality rejected/非零首样本计数器及历史 manifest 失败语义。manifest JSON 损坏、快照缺失、无效单位按需链路专项通过；无虚假 READY。gap 连续滤波 NaN 传播沿用旧科学契约，报告明确其限制。
- [x] 并发 ensure 只执行一次；不同源 hash、通道顺序、滤波/陷波、窗口策略或科学身份不共享结果。准备隔离、Definition 防伪及完整窗口 source/config/policy/build 隔离专项通过；不跨身份复用 READY。
- [x] 验证 cancelled retry、多个 subscriber、最后订阅者退出、worker crash/restart、partial publish、ready corruption。仓储/API 注入和真正 subprocess kill/restart 共 2 项恢复测试通过；恢复不需要客户端再次 ensure。
- [x] 验证完整 Run、`/api/runs/ensure`、cancel/retry、structured preview、window-preview、export 与旧 artifacts 回归。`test_on_demand_psd_api.py` 验证旧完整 Run 全链路及 source/artifact hash 不变。
- [x] 测短/长合成记录冷/热注册、读取、连续预处理、单窗计算、发布/读取时延及峰值内存/临时磁盘；不得把准备时间隐藏在首窗指标外。60 秒/3600 秒连续与 gap 场景测量已保存；冷指标 n=1、RSS 20 ms 采样和 TestClient 限制已披露。
- [x] P1 科学、兼容和资源证据通过后才显式启用验收开关；当前源码启动脚本开启 PSD，用户完成本轮 PSD GUI 复验。源码开关默认仍关闭，其余算法通过成对环境变量验收，不自动批量开启。

## P2：WPF Review 与算法详情垂直切片

- [x] 增加类型化窗口 API client/contract，保留旧 Run/window-preview client 方法。WPF `IAlgorithmClient`/`HttpAlgorithmClient` 已接入 ensure/status/subscribe/cancel，旧接口未改动。
- [x] 扩展共享 algorithm-neutral coordinator/provider capability，整合 window identity、subscription 生命周期、latest-only 竞态和缓存；Review 不认识 PSD 数据数组。按需入口通过 provider 适配，Review 仍只消费窗口投影。
- [x] Review 与算法详情分别实现轻量 target adapter，共用 coordinator；复用唯一 RecordingRelativeSampleIndex 游标，不创建第二个 timer/clock。`DynamicWindowTargetCoordinator` 仅协调传输；Review provider 与 Catalog.OnDemand 复用，详情/Review 专项测试通过。
- [x] PSD Presenter 解析 ready result 并绑定 PSD 模块图表；不得重算滤波、PSD、质量或频带。PSD provider 将后端数组/单位映射到既有 SciChart 投影，WPF 未实现科学计算。
- [x] 实现目标/已显示旧窗口/实际窗口独立状态、加载、insufficient、rejected、unavailable、cancelled、failed 展示。准备/计算状态经真实轮询回调展示；详情质量证据支持实际 failure.details，PSD 显示单位显式转换为 uV^2/Hz；GUI 渲染验收另列 P3。
- [x] 算法列表/详情页的全量与按需正常交互按本轮逐算法用户反馈通过；验收指引包含播放、暂停、跳转、模式切换和结果对应，未逐项采集请求日志或性能数据。
- [x] 用户在 Review 倍速、切记录、关闭/重开面板及波形独立性验收指引后反馈“可以正常”；登记人工综合通过，不新增帧率保证。协调器的去重、配置隔离、关页和晚返回竞态继续以自动化为证。
- [x] 验证取消一个页面订阅不影响其他订阅者，页面卸载不删除持久科学结果。后台共享订阅测试和 WPF release/close-reopen 测试已通过；页面释放只取消自己的 subscription。

## P3：真实记录验收和性能决策

- [x] 使用用户本机真实 WPF 记录比较完整 Run 与按需窗口，保存脱敏诊断和可重复验收报告。2000 Hz、前 2 个导联共 114 READY 窗口，误差 0；source/artifact hash 和完整 evidence 比较已保存，不提交 EEG 或个人路径。
- [x] 用小时级合成记录测目标随机跳转、顺序播放、gap、多通道和多请求；报告 p50/p95 首窗/后窗/准备耗时、总耗时、请求数、CPU/峰值内存/临时磁盘。200 Hz、2 通道、4 消费者；后窗/并发分布及阶段耗时完整。冷首窗/首次预处理仅 n=1，不伪称有统计 p95；高采样率/几十通道不在本报告保证范围内。
- [x] 自动化证明一活动+一可替换目标、有界请求/轮询及 backend fault 可观察/可重试；用户在后端停止时波形独立播放及恢复验收指引后反馈“可以正常”。没有新增延迟或吞吐保证。
- [x] 使用 hash 证明原始记录、历史 Run/artifact 未改写；验证功能开关关闭后完整 Run 路径可用。真实格式合成源 API 测试比较 source/artifact bytes，WPF opt-out 专项验证旧 Run 路径；真实用户记录证据另列上方。
- [x] OpenSpec strict、backend 全量测试、WPF 全量测试、Release build、文件规模检查、`git diff --check` 均通过。2026-10-13 最新复核 backend 659/659，WPF 645/645，Release 0 errors、既有 NU1701 警告；strict/规模/diff 通过。WPF 共享 Application 生命周期已修复，不再把全量资源初始化失败当作环境问题跳过。

## P4：逐算法迁移（PSD 自动化门通过后逐个进行，GUI 最后统一验收）

2026-10-13 用户已确认调整顺序；依据见 design 第 8 节。默认生产开关不打开，
每模块独立验证，未迁移者保持旧模式。以下细项不是批量开启授权。

- [x] RBP：单窗五频段功率、固定滤波/参考来源、质量和完整 Run 数值/证据对照；独立 WPF 投影。
- [x] 频段功率比：独立频段配置、正分母门与单窗 evidence 对照；独立 WPF 投影。
- [x] 频段峰频率：频段/网格/并列峰政策、质量对照；独立 WPF 投影。
- [x] IAPF：峰估计/不可用原因和残差 evidence 对照；独立 WPF 投影。
- [x] Theta/Beta：个体峰与双频段关系、质量对照；独立 WPF 投影。
- [x] FAA：成对源、连续预处理/epoch/最小干净 epoch 和数值对照；独立 WPF 投影；阶段 3 和 2026-10-15 本轮正常路径 GUI 均获用户确认。
- [x] Brainbeat：双通道/派生依赖及独立窗口无跨窗 EMA 对照；独立 WPF 投影；阶段 4 和 2026-10-15 本轮正常路径 GUI 均获用户确认。
- [x] STFT：内部时频坐标、外层窗、shape/质量与完整 Run 矩阵对照；独立 WPF 投影；阶段 5 和 2026-10-15 本轮正常路径 GUI 均获用户确认，源码开关默认仍关闭。

- [x] 已迁移的 RBP、四标量模块、FAA、Brainbeat 与 STFT 分别完成 science/capability 评估与 fixture/真实格式数值验证；生产开关仍默认关闭。
- [x] FAA 单独验证 continuous preprocessing 与 per-window epoch 语义，不能复用错误 prepared-source 实现；窗口结果保留 `whole_recording_before_window_slice` provenance。
- [x] 更新能力矩阵、模块接入清单与阶段 2 验证报告；未迁移算法保持旧模式并明确说明。
