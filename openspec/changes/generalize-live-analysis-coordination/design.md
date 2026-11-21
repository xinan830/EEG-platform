# Design

## Context

See proposal.md for motivation. The live recording-access session already owns bounded V/float64 sample storage, source identity, channel metadata, sample counters and gap-aware reads. The current live RBP service adds algorithm-specific configuration, session/window scheduling, latest-result ownership and RBP calculation in one service. Offline Run orchestration has different persistence and lifetime semantics and remains separate.

## Goals / Non-Goals

**Goals:**
- Share live session scheduling and result ownership across windowed algorithms.
- Keep sample alignment, gap handling, queue bounds, cancellation and stale-result rejection independent of an algorithm's science.
- Let a new algorithm register a typed adapter and present its own result without editing device capture or the acquisition coordinator.
- Preserve RBP's current request/result values, implementation identity and review chart behavior.

**Non-Goals:**
- Unify live execution with offline Run, playback, or review coordinators.
- Add a general plugin loader, cross-algorithm dependency graph, stateful streaming protocol, persistence, or algorithm catalog UI.
- Change acquisition buffer ownership or duplicate the bounded source in the desktop client.

## Decisions

1. **Keep one bounded source per acquisition session.** `LiveRecordingAccessSession` remains authoritative for sample counters, channel metadata, V units, gaps, and reads. Transport batches only trigger the coordinator to inspect due endpoints; batch size and arrival time do not define scientific windows. Alternatives: a new common data store (duplicates ownership) or direct reads from device callbacks (couples execution to capture).

2. **Add a backend live algorithm registry and coordinator for windowed algorithms.** A registered adapter validates its configuration and input requirements, declares the normalized channel order/window/step, and computes a validated sample block. The coordinator owns session identity, endpoint scheduling, bounded pending work, cancellation, current revision, and close. Keep each adapter independently testable; do not branch on `algorithm_id` in scheduling code. Alternatives: copy `LiveRbpAnalysisService` for every algorithm (duplicates lifecycle) or place formulas in the coordinator (mixes ownership).

3. **Use a versioned common update envelope with an algorithm-owned payload.** The envelope identifies message type, schema, acquisition session, algorithm, implementation, revision, status, and sample/time bounds. Payload schemas remain validated by their algorithm adapter and presenter. Keep the current RBP payload shape under that envelope during migration; do not make the generic coordinator interpret band values. Alternatives: a single rigid result model (cannot express different algorithms) or an untyped dictionary across all layers (weak validation).

4. **Reuse the existing sample transport and expose generic algorithm commands.** The desktop sample bridge publishes the same accepted raw samples and can start/stop a registered algorithm by identity. The acquisition core does not know RBP settings or other scientific parameters. RBP's panel remains an algorithm-specific presenter using the existing shared RBP chart. Alternatives: add one endpoint and one bridge branch per algorithm (forces core edits on each integration).

5. **Migrate RBP behind behavior-preserving tests.** Compare the old and new RBP result envelopes for deterministic sample windows, including identity, bounds, channel order, values, units, quality and provenance. Remove the old RBP-owned scheduler only after these tests pass. No migration of saved data is needed because live results are ephemeral.

## Risks / Trade-offs

- [A generic interface can become a plugin framework before a second algorithm needs it] → Support only windowed, stateless adapters now; add streaming modes only for a concrete algorithm.
- [A shared scheduler can mislabel gaps or stale results] → Preserve canonical sample bounds and test gaps, overwrite, overload, cancellation, and reversed completion order.
- [Envelope migration can break the existing WPF panel] → Keep a versioned RBP payload adapter and test the real HTTP request/response bridge.
- [A backend adapter failure could affect other algorithms] → Isolate state and typed failure per algorithm session; capture/raw persistence remain outside the coordinator.

## Migration Plan

1. Add contracts and coordinator alongside the existing RBP service, then register RBP as the first adapter.
2. Switch the local live-analysis endpoint and desktop bridge to the common envelope; preserve existing RBP user controls and panel.
3. Run deterministic backend and desktop contract tests and simulated acquisition-stream tests; then remove superseded RBP scheduling code.
4. Rollback by restoring the existing RBP endpoint/bridge while keeping saved recordings untouched. No database migration or offline result conversion is required.

## 当前已实现的执行与传输基线

- Each acquisition source keeps its existing V/float64, sample-major retention store. The coordinator reads an owned `SampleBlock` snapshot in the worker, gates missing/overwritten ranges before science, and passes that block to the adapter. RBP's window-local preprocessing, five-band formulas, channel quality, provenance and identities are unchanged.
- The executor has four global slots and at most one in-flight window per active algorithm. Sample ingestion does not await calculation. Due endpoints stay on the configured sample grid; if multiple endpoints are already due, only the newest is scheduled and `skipped_windows` counts the superseded endpoints. If a worker/algorithm slot is busy, the current window returns `overloaded` with `LIVE_ANALYSIS_OVERLOADED`. There is no unbounded pending queue.
- 以下是本轮补充实施前的窗口基线。启用时从当前最高样本计数后开始累计，结果秒数以算法启用后的首样本为原点。滚动启动目标见下节。
- Restart/stop/close invalidates the active algorithm object. An already running scientific call can finish, but object identity and monotonic revision checks prevent its result from publishing. Cancellation does not forcibly abort a native scientific call; its bounded worker slot is released on completion.
- The only current local consumer is `HttpLiveSampleAccessBridge`. It and the backend migrate together: `POST/GET/DELETE /api/live-recordings/sessions/{session_id}/algorithms/{algorithm_id}` replace the unreleased RBP-only endpoints. Start accepts `live-analysis-config-v1` with algorithm-owned `config`; responses use `live-analysis-update-v1`. Sample acknowledgement becomes `live-sample-ack-v2` with an `updates` array. Both processes must restart from the same source version. No old-format reader, second scheduler, or persistent migration remains.
- Valid RBP updates preserve their existing `live-rbp-result-v1` fields under `payload`. Common gap/overload/worker failures carry no scientific payload and use structured failure codes; the RBP presentation adapter renders an unavailable chart point instead of zero or stale values. The sample bridge and acquisition runtime contain no RBP-specific configuration or payload fields.
- Unknown algorithm updates are ignored unless explicitly enabled. The UI routes by algorithm identity; `LiveRbpPresentation` validates RBP identity, units, channel order, sample duration and envelope/payload consistency before updating the existing shared chart.

## 滚动窗口启动计划

1. **在采集开始时保留样本。** 预览和录制期间复用桌面现有的有界样本分发器与后端 `LiveRecordingAccessSession`，从当前采集会话的第一批样本开始上传。样本为原始 EEG，按通道顺序传输 `float64`、V、sample-major 数据。沿用固定时长聚合和异步队列，受容量限制的后端缓存覆盖旧样本。确认容量按实际采样率至少容纳 10 秒。RBP 关闭期间缓存继续滚动，算法任务随开关启停。停止或故障结束采集会话时关闭缓存，禁止跨会话取样。
2. **分开记录采集起点和保留区起点。** 每个采集会话保存首个接收样本的真实计数 `S0`。后续缓存覆盖及 RBP 重新开启沿用 `S0`。窗口保留真实半开样本边界 `[start_sample, end_sample)`，结果 `start_s` / `end_s` 统一按 `(边界计数 - S0) / 采样率` 计算。结果封装与 RBP payload 使用同一个零点。采集时间包含采样计数器显式记录的缺口。
3. **启用时立即安排最近窗口。** 用采样率换算窗口 `W` 与步长 `P`，沿用现有取整。以最新已接收样本计数加一作为候选终点 `E`。达到窗口长度时，启动命令使用现有有界工作槽安排 `[E-W, E)`，后续终点为 `E+P`、`E+2P`。启动时采集未满 10 秒就进入预热，首个候选终点为 `S0+W`，之后同样按 `P` 推进。跳过重复窗口，过载仍有结构化状态。RBP 默认 `W=10` 秒、`P=1` 秒，使用同一个 `five_band_shares()` 计算核心。
4. **沿用缺口和结果传输边界。** 最近窗口跨缺口或读取时已被覆盖，发布带实际边界的不可用状态，直到后续滚动到完整窗口。启动计算异步进行，通过已有 `updates` 应答和最新结果接口取结果。正常持续采集时，完成后随下一批应答送达界面。没有后续样本的状态若仍允许启用，通过现有最新结果接口做有界、可取消的首结果查询。停用、重启或切换会话可取消等待，旧会话与旧版本结果不得更新图表。
5. **验证常驻传输成本。** 复用后台缓存可避免启用瞬间从桌面波形缓冲回填及再拼接。新增代价是 RBP 关闭时仍有本地样本传输，检查禁用状态的内存、CPU、HTTP 批次和原始样本连续性。先做模拟数据测试，再用真实设备连续录制，核对样本计数、写盘、波形与回放。

同步部署桌面端和后端，沿用当前消息结构。回滚时成对恢复开启后上传与从启用点计时的规则，无需迁移录制文件。重点复核采集协调器与工作区 ViewModel 的行数登记，优先在现有样本访问与调度模块内完成。

## File-size review

New backend contracts/coordinator/adapter and desktop transport/presentation files are below 400 lines. `ConfiguredAcquisitionRuntime` remains one session lifecycle owner (448 lines). The pre-existing acquisition workspace ViewModel is 1186 lines and retains the executable split plan in `docs/code-size-policy.json`: extract device setup, live presentation and event commands into dedicated owners before further feature growth. A broad ViewModel rewrite is outside this transport migration because it would expand capture lifecycle risk. No acquisition loop or raw writer changes are required for adding another registered windowed adapter.
