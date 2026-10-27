# 按需分析分阶段计时

范围：只增加诊断日志字段，不改变调度、算法结果、时间/单位契约或 API 响应。`skip_specs: true`。

新版本后端启动后，检查 `storage/logs/backend-YYYYMMDD.log`：

- `dynamic_window_queued` / `dynamic_window_cache_hit`：准入总耗时、输入解析、窗口规划耗时。
- `scientific_compute_acquired`：交互/批处理 lane 的计算槽等待时间和等待队列快照。
- `dynamic_window_source_prepared`：worker 到准备完成的时间，以及独立的计算槽等待时间。worker 时间不应解释为纯 I/O 时间。
- `tnk_on_demand_execution`：目标帧、检查点、重放帧、频谱加载/命中、子算法计算和重放耗时。
- `dynamic_window_computed` / `dynamic_window_ready`：执行区间、artifact 写入和数据库发布耗时。

验证：按需分析服务测试、计算协调器测试和 TNK 检查点测试通过；一次 TNK 质量拒绝断言仍失败，失败位于已有未提交的 TNK 科学行为改动范围，需独立处理。回滚只需撤销本次计时字段和 logger 命名空间变更；不触及已有用户改动。

真实录制验证（`6.16高发展`，718 s、500 Hz、21 个 EEG 通道，PSD/F3，5 s 窗口、1 s 步长）：10 个连续目标全部 `ready`。首个目标端到端约 645 ms，其中源准备约 579 ms；后续目标端到端约 90--140 ms，中位数约 98 ms。准入中位数约 18.8 ms；计算槽等待约 2.5--2.8 ms；科学计算约 6--16 ms；artifact 写入约 23--25 ms。重复同一目标的缓存命中约 10.3 ms。该数据说明当前录制上数值计算不是主要瓶颈，首个源读取/初始化和轮询等待更值得继续测量。

## 2026-10-26 按需通道准备优化

对同一份 `6.16高发展` 录制（718 s、500 Hz、21 个 EEG 通道）拆分测量显示：WPF
清单检查约 39 ms，全记录读取约 72 ms；连续预处理全通道约 374--383 ms，单个请求通道
约 85--87 ms。单通道输出与全通道输出逐样本一致（最大绝对差 0）。因此按需 PSD
现在只为请求通道建立连续预处理缓存；静态分析仍保留全通道缓存复用。

真实服务复测：全通道静态冷启动 487.2 ms，按需单通道冷启动 100.6 ms，按需缓存命中
5.8 ms；准备矩阵从 `(359000, 21)` 降为 `(359000, 1)`。PSD 和频带功率与全通道参考
的最大绝对差均为 0。

同一按需通道投影也应用到 STFT 连续预处理入口。静态与按需的 STFT 线性功率及 dB
矩阵在合成双通道数据上逐元素一致；实际动态目标通道只保留在准备矩阵中。

## 2026-10-26 真实录制其他算法按需验证

使用同一份 `6.16高发展` WPF 录制（recording id
`wpf-montage-8b05670b-6703-484d-b236-0532463e0efa-6aff657478fb`，718 s、500 Hz、21
通道），在隔离临时 catalog/artifact 中通过 `DynamicWindowService` 和运行队列验证，未改写
源录制。对照全量动态结果和按需目标窗，目标窗取 25--30 s（FAA 的 20 s 窗取 10--30 s）。

| 算法 | 按需状态/质量 | 全量对照 | 最大绝对差 |
|---|---|---|---:|
| RBP | ready / clean | 5 个频带值一致 | 0 |
| Band Ratio | ready / clean | 比值一致 | 0 |
| Peak Frequency | ready / clean | 频率一致 | 0 Hz |
| IAPF | ready / clean | 频率一致 | 0 Hz |
| Theta/Beta | ready / clean | 比值一致 | 0 |
| Brainbeat（Fz/Pz） | ready / clean | 比值一致 | 0 |
| FAA（F3/F4） | ready / clean | 配对不对称值一致 | 0 |
| PSD（F3） | ready / clean | 先前真实录制测试已逐窗比较 PSD、band share 和证据 | 0 |
| STFT（F3） | ready / clean | 时间轴、频率轴、线性功率、dB 功率、质量和 provenance 均与全量窗口一致 | 0 |

这证明列出的按需算法在该录制、所测参数和窗口上可运行；表中相等也只代表与当前全量
执行路径对照，不构成独立科学实现的验证。TNK 整组未纳入：该录制没有 Oz，不能用 O2
替代；FAA 单指标仍可用真实 F3/F4 输入单独验证。STFT 的真实数据矩阵级 parity 已补齐。

桌面协调器新增可选队列诊断（环境变量 `BRAIN_REVIEW_PERF_DIAGNOSTICS=1`）：
`review_analysis_queue` 记录活动游标、最新排队游标和 pending 数量，用于把“后端处理慢”
与“播放请求已经积压”区分开。该诊断不改变调度、科学结果或 API 契约。

验证：后端光谱/按需相关测试 45 项通过；桌面 `DynamicWindowTargetCoordinatorTests`
16 项通过；`git diff --check` 通过。测试运行产生的现有 .NET SDK/包兼容警告未影响结果。

## 2026-10-26 后端 1x/2x/4x 目标到达压力验证

使用上述真实录制，新建隔离 catalog/artifact，每个速度连续请求 12 个相邻 1 秒目标，
目标请求的墙钟间隔分别为 1.0、0.5、0.25 秒。1x、2x、4x 均为 12/12 `ready`，未观察到
请求错误；每种速度的最高待处理数为 1，最后完成游标与最后请求游标相同。整段请求耗时
分别约 12.09、6.10、3.13 秒。

这是直接调用后端 `DynamicWindowService.ensure/get` 的到达压力测试，证明该后端单 worker
在这份录制和 PSD 单通道配置下能跟上 4x 目标节奏。它没有启动 WPF 播放器，也不覆盖
Dispatcher/UI 绑定、HTTP 网络抖动或不同算法；桌面新增的 `review_analysis_queue` 日志仍需
在真实 WPF 播放时采集，才能对端到端队列滞后下结论。

## 2026-10-26 跨算法按需定位字段

通用按需 worker 的 queued/cache-hit/source-prepared/computed/ready/failed/cancelled/lease-lost
事件现在关联 `algorithm_id`、`recording_id`、`request_id`、`window_id`、`attempt_id`，并记录
目标和上下文的半开样本范围。失败事件包含执行阶段；取消和租约丢失单独记录，避免误判为
科学算法异常。通用 scientific compute coordinator 继续只记录 lane/等待状态，不引入算法领域字段；
TNK 仍保留自身 checkpoint/replay 子阶段计时。

这些日志只提供后端队列和执行链路定位，不能单独判断 WPF 绑定、HTTP 轮询或绘图是否正常；
真实播放仍需结合桌面 `review_analysis_queue` 和结果绑定诊断。验证：按需服务及失败注入测试
18 项通过，`git diff --check` 通过。
