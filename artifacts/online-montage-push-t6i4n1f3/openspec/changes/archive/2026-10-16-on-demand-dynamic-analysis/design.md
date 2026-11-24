# 设计：按需动态分析窗口

## 1. 基线与边界

本设计以 `docs/architecture/on-demand-dynamic-analysis-plan.md` 为详细实施背景。当前已有：动态窗口策略/窗口模型、完整 Run 队列及进度、按配置缓存、完成 artifact 的窗口索引/只读读取、PSD 回溯 provider 和游标 latest-only 绑定。它们都不能被误当成“运行中按需窗口计算/发布”。

必须保持的旧能力：`POST /api/runs`、`POST /api/runs/ensure`、run 状态/取消/重试、完整 artifact、导出、`structured-preview` 及 `GET /api/runs/{run_id}/window-preview`。新能力使用独立资源与状态契约，不能改变旧 `RunStatus`。

## 2. 分层职责

```text
Review / Algorithm detail
  -> shared dynamic-window client coordinator (latest target, subscriber lifecycle)
  -> ensure-window API (identity, authorization, dedupe, bounded queue)
  -> generic dynamic-window service (state/persistence/atomic publication)
  -> declared algorithm capability (plan one window + science-specific compute)
  -> source/preprocessing provider (retains the algorithm's declared semantics)
```

- 通用服务处理稳定身份、权限/项目域、任务去重、队列配额、状态、取消订阅、原子结果记录和诊断；不能含 PSD/F​​AA 等算法 ID 分支。
- 算法能力定义后端窗口计划、必需依赖、执行入口和结果 schema；保留公式、质量规则和预处理契约。能力缺失时不得隐式猜测或用完整 Run 伪装成功。
- 前端只提交 recording、算法定义/配置、权威游标 sample 和请求身份；不能指定权威科学窗口边界或计算科学数组。
- Review 保持唯一游标和播放时钟。共享客户端协调目标请求、同目标等待、取消当前订阅及 stale response 隔离；PSD 模块解析 PSD 数据并负责图表。

## 3. 身份与存储决策

正式实现前以 P0 设计评审冻结 schema。候选最小身份为：项目/访问域、recording ID 与源 hash、定义 ID/version/digest、scientific version、implementation identity/build、规范化完整科学 config digest、窗口策略版本、start/end sample。显示导联顺序、游标、播放速率、图表模式和仅显示滤波不得进入键；真正进入分析的有序源通道/参考和预处理参数必须进入键。

一个**配置会话**标识配置身份与共享源准备；一个**窗口结果**是不可变的窗口记录/产物。不要将逐窗结果 append 到既有 completed Run 或覆写现有 NPZ。存储需支持：唯一配置会话、唯一窗口键、queued/running/terminal 状态、申请者订阅、lease/worker 恢复信息、单窗结果 schema、artifact/checksum、创建与结束时间、可诊断的错误码。失败重试创建新 attempt/request identity；不得永久绑定一个被取消的幂等键。

窗口规划分两步：服务先依据请求建立不含样本范围的 session identity，算法 capability 只根据游标和自身窗口策略返回边界；服务随后将实际半开样本范围合并为 window identity。这样算法模块不能伪造配置/session 身份，也不会在尚未规划窗口前猜测其边界。

### P0 schema draft (before migration)

The following is the reviewable minimum; names are internal and may only be
changed by updating this design and its migration tests together.

| Entity | Required identity/fields | Invariants |
|---|---|---|
| `dynamic_analysis_sessions` | `session_id`, `project_id`, `recording_id`, `source_sha256`, algorithm/definition/scientific/implementation identities, normalized `config_sha256`, `window_policy_version`, `status`, timestamps | one session per exact source/config identity inside a project scope; display state and cursor excluded |
| `dynamic_analysis_windows` | `window_id`, `session_id`, `generation`, `start_sample`, `end_sample`, `start_s`, `end_s`, lifecycle `state`, `quality`, `failure_json`, `result_artifact_id`, timestamps | unique `(session_id,start_sample,end_sample,generation)`; half-open positive range; READY requires verified artifact; terminal state cannot reopen |
| `dynamic_analysis_attempts` | `attempt_id`, `window_id`, request/lease identity, `state`, worker/heartbeat, error, timestamps | retries get a new attempt; at most one active lease per window; stale lease can be recovered |
| `dynamic_analysis_subscriptions` | `subscription_id`, `window_id`, `request_id`, project/user scope, `state`, timestamps | a subscriber can cancel itself; cancellation does not cancel shared work with other active subscribers |

Foreign keys are scoped and indexed for recording/project lookup. Scientific
JSON is canonicalized before hashing; raw payload arrays are not stored in
SQLite. Artifact publication and the READY state transition must commit under
one transaction after the temporary payload checksum has passed. Migration
tests must cover duplicate identities, rollback, missing artifact, stale lease,
restart recovery, project isolation, and old databases with no new tables.

发布顺序：在派生存储写临时数据 -> 校验 schema、shape、单位和 checksum -> 原子 rename/commit 元数据 -> 状态转 ready。崩溃留下的未提交临时文件不可读且可回收；READY 记录缺 artifact 或校验失败时返回结构化 corruption 状态，不返回半成品。源录制始终只读。

旧完整 Run 是否可作为按需结果的来源不做隐式转换。第一版可以只复用已验证的同键窗口结果；若希望从已完成 Run artifact 导入窗口，必须在 P0/P1 明确采用既有 bounded reader、验证身份并将结果复制/索引为新不可变窗口记录，不能把全量 artifact 读入内存后切片。

## 4. API / 生命周期草案（P0 冻结字段）

候选：`POST /api/analysis-windows/ensure` 返回 202，给出 session/window ID、实际后端计划范围、当前状态、request ID 和轮询位置；状态/结果读资源按 ID 获取。ready 响应仅含目标科学输出、数组轴/shape/unit、实际 sample/time 范围、quality/evidence、source/config/version provenance 和完整性状态，不含原始 EEG。

服务端根据 cursor sample 与算法策略唯一生成 window ID 和半开 sample 范围。越界/非整数/非有限游标、不可映射 Recording、能力不支持、项目不匹配使用稳定结构化错误。数据尚不足返回 insufficient-data/等待状态，不选择未来窗；gap、rejected、unavailable 与执行 failed 独立表达。API 不同时接受冲突秒和 sample 坐标；秒仅是可选兼容输入且须由后端统一 floor 转换。

共享工作和订阅者是不同生命周期：相同窗口键并发 ensure 合并成一个工作；每个请求者拥有独立 subscription。取消订阅不表示取消共享任务。最后一个订阅者退出后，服务可以取消尚未开始的工作；运行中的科学调用仅在能力声明可安全检查取消且没有消费者时停止。取消不能回滚已原子发布的 READY 窗口。用户显式取消的权限/项目域必须由后端验证。

配置预处理可能比窗口计算昂贵。应独立跟踪 source preparation 与窗口状态/耗时；并发/复用边界按 source+预处理身份管理，但不同配置不能共享不匹配预处理。首期不允许为了按需窗口私自局部零相位滤波。

## 5. 有界调度与淘汰

### 首期资源预算（2026-10-13）

小时级 200 Hz、2 通道实测预处理临时文件峰值约 46 MB；四并发消费者未形成无界积压。
首期采用单按需 worker、全局 16 / 项目域 8 / 单 recording 4 个活动 attempt，
相同任务新增订阅不重复占配额，无 prefetch。项目域包含未指定 project 的本地域。
生产 CPU 原生线程继续由 `RunWorker` 的进程级单一 owner 限制为 1，不新增嵌套 threadpoolctl owner。

单次按需连续预处理最多分配 8 GiB 临时映射文件；分配前检查剩余磁盘，保留 1 GiB，
读取/滤波 block 目标不超过 8 MiB（不把整段搬入 RAM）。这不是录制时长上限；
资源不足返回 `WINDOW_RESOURCE_EXCEEDED`，不截断或改变滤波。
数值 artifact 上限 16 MiB、元数据 2 MiB，按需派生目录预算 2 GiB。
新工作 admission 为最多 16 个活动工作保守保留每项 18 MiB 的发布空间，满额返回 429，
READY 读取和已有任务订阅仍可用，不自动淘汰已发布科学结果。
这些为可调整的首期保守默认值，不是全进程 RSS 硬上限；高采样率/更多通道需独立压测。
旧完整 Run、其他进程磁盘使用及操作系统页缓存不在单窗口预算内。

### Explicit retry and recovery

`ensure` resolves the newest execution generation, without reopening terminal
rows. Only `cancelled` and execution `failed` can be retried explicitly. A retry
names `retry_of_window_id` and supplies a new client `request_id`; the transaction
creates generation N+1 once. Concurrent/replayed requests naming N resolve to
that same successor, even if N+1 has already failed. Retrying N+1 requires naming
N+1 explicitly. Generation is execution provenance, not a change to scientific
configuration or algorithm version. Migration v17 preserves generation-zero
rows and foreign-key references from existing attempts/subscriptions.

Lease expiry preserves the expired attempt and atomically resets an unpublished
`preparing_source/computing_window` to `queued`. Final publication/state updates
must validate the active token in the same transaction; a pre-publication check
alone is insufficient. READY rows and their artifact references are immutable.

interactive target 优先于可选 prefetch；每项目、每 recording 和全局有有限在途任务/队列上限。前向预取最多一个邻窗，首期默认关闭直至测量证明可用。worker 并发、CPU/内存/临时磁盘上限通过长记录压测确定，不能按机器核数无限扩张。相同窗口键 in-flight 去重；热点缓存读取不创建任务。

磁盘保留策略必须区分用户可见已发布科学结果、可重建准备缓存和临时文件。首版不可自动删除用户可能正在查看的已发布结果；若加入容量回收，需明确引用/活动订阅保护、项目隔离、保留期限、审计与恢复。禁止简单以 LRU 静默删掉唯一持久科学结果。

## 6. 科学等价与验证

PSD 对照必须使用同源 bytes、源通道与顺序、记录坐标、采样率、参考、滤波参数、分析范围、窗口/步长、算法定义/科学版本/实现身份。按相同窗口逐项比较 start/end sample、PSD frequency grid、数值矩阵、单位、state、quality/evidence、失败原因及 preprocessing provenance；预先声明 `rtol/atol` 并报告最大绝对/相对误差、失败点。

特别验证连续 zero-phase 的边界/gap 行为：按需读取不得截取局部 EEG 后重新 `filtfilt`。如果当前连续预处理必须对整条 recording 执行，准备耗时仍可能随录制长度增长，应把它单独报出。结果正确性、首窗延迟、全量总耗时和资源是四个不同结论。

后续算法先做能力清单和科学契约分类。每个算法独立提供同配置完整 Run 对照；对 FAA 等 preprocessing scope 不同者，不允许复用 PSD 的 prepared-source key 或函数，除非语义一致且有独立证据。

## 7. 迁移与回滚

数据库新增 migration，不改现有 Run/artifact 行的含义；重启恢复只处理新窗口任务，分别恢复 queued 与 lease 过期任务；READY 结果不重新计算，校验失败显式标记。旧记录/旧 Run 无需升级。

按算法 capability flag 切换新旧路径。关掉 flag 后，已有详情/回溯可使用当前完整 Run；旧 API 永远保留。可重建缓存与临时数据可以清理，但不触及源 recording、原 manifest、原始审计、历史 Run 或既有 artifact。

## 8. 实施阶段

### 最新交付顺序（2026-10-13，用户要求）

用户后续改为“同步更新 tasks，一个阶段一个阶段完成后验收”。此要求覆盖下方
此前统一验收的顺序：先完成四标量 WPF 接入与自动化验证，等待阶段 1 用户验收；
随后分别交付证据文档、FAA、Brainbeat、STFT 与最终收口。每阶段验收前不推进下一阶段。
生产开关保持默认关闭，历史决策保留用于追溯，当前执行状态以 tasks 的阶段清单为准。

### 顺序调整决策（2026-10-13，用户确认）

用户明确选择“调整顺序，继续逐算法迁移，最后统一验收”。因此 P4 不再以
PSD 的人工 GUI 验收为开发前置条件，而以 P1/PSD 科学等价、兼容、资源和工程
自动化门通过为前置条件。每个算法仍须独立评估、独立数值对照后才可声明能力；
生产开关保持默认关闭。P2/P3 GUI 项保持未完成，移到全部算法实现后的统一验收，
不以自动化测试代替，也不因此把其他算法或整体功能标为已验收。

1. **P0 契约**：能力 protocol、schema/API/error/state、稳定身份、SQLite migration 草案、订阅/取消/权限、原子发布、资源上限决策和测试矩阵冻结；只增加协议/模型及测试，不接生产执行流。
2. **P1 后端 PSD 核心**：能力注册与验证、持久化 ensure/state/result、共享预处理身份、有限 worker/去重/取消、崩溃恢复与 PSD 单窗计算；完整 Run 未删除。
3. **P1 验证门**：数值/质量/provenance 对照、旧 API 回归、并发/项目域/部分发布/恢复和长记录资源验证通过后，才能启用 UI。
4. **P2 WPF 垂直切片**：共享窗口 API client/coordinator、Review 与 Algorithm detail target adapters、latest-only、质量与错误；PSD handler 独立。
5. **P3 真实验收**：真实记录/长合成记录和资源自动化已通过；GUI 播放/快进/倒退/跳转/暂停/关页验收留到全体实现后。保持未验收标记与默认关闭开关。
6. **P4 逐算法接入**：PSD 自动化门通过后逐一登记 capability、科学语义和验证证据；全部实现后统一 GUI 验收。无能力模块继续旧模式，不能因通用 service 已存在就批量标记支持。

## 9. 重要备选方案

### 标量模块迁移契约（2026-10-13）

频段功率比、频段峰频率、IAPF、Theta/Beta 复用模块原有单帧计算，共享适配器
只封装一项标量及 `time_s=[window.end_s]`。各模块明确声明并独立验证连续预处理
后取窗；共享封装不得推测其他算法的 preprocessing scope。Partial 判定采用本次
配置的窗口长度。首窗不足无数组；质量拒绝和数学不可用保留原 Run 的 failure、
quality 与 evidence，HTTP 非 ready 响应不返回科学数组。

Theta/Beta 原实现、历史结果和测试使用 `dimensionless`，manifest 曾误声明
`ratio`。本次校正 manifest 与按需 schema 为 `dimensionless`，不修改公式、历史
Run 或 scientific version；manifest digest 随声明改变，不复用旧声明的执行身份。
该调整是元数据一致性修复，不是数值科学版本变更。

- **只改 WPF 调度**：无法产生尚未计算的窗口，也不能安全地保存/去重，因此不满足目标。
- **每个游标发完整 Run**：会重复全量计算、生成大量 Run 并打满队列，拒绝。
- **在现有 completed Run artifact 追加数据**：破坏不可变历史、checksum 与并发读语义，拒绝。
- **只读完整 NPZ 后取一窗**：算法结果虽然正确，但大记录会将内存与延迟问题搬到读取侧，不满足有界要求，拒绝。
- **窗口独立滤波或 causal filter**：可能改变边界值、相位、延迟和数值，超出当前等价重构，拒绝。
- **新独立窗口 store/session**：首选方向，但需 P0 验证 SQLite 迁移、项目隔离和恢复边界后定案。
