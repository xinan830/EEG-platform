# 长记录回溯性能 Phase 0 证据记录

日期：2026-10-18  
OpenSpec：`improve-long-recording-review-performance`  
当前提交：`748044f`（性能诊断入口、回溯阶段采样与算法结果来源采样）

本地未提交增量：`FilteredSource` 现在记录每个 10 秒滤波源块从缓存读取/滤波请求
开始到缓存持久化完成的实际耗时；对应回归测试已加入。

## 当前已证实

- 本轮之前最近一次 WPF 桌面测试通过：728/728；本轮验证结果见文末。
- `git diff --check` 通过。
- 回溯会话现在以 opt-in 方式记录导航目标、原始读取、滤波源、帧构建和
  SciChart 绑定阶段。
- 算法动态窗口现在以同一开关记录全量结果的本地指标适配、结构化预览缓存、
  索引化 `window-preview` 读取、按需计算和全量回退到按需这几类来源，并携带
  算法/Recording/Run/请求身份、窗口状态和耗时。
- 诊断样本包含 `recording_id`、`review_session_id`、revision、绝对时间范围、
  来源、通道数、点数和耗时；不包含原始 EEG 数值、完整响应或凭据。
- 现有回溯缓存仍有明确上限：原始窗口 2 个、帧 3 个。绘图传入的 `2000`
  是每段的桶数预算，不是每条轨迹的硬点数上限；每桶保留时间有序的首尾和极值，
  多段、无效样本和 gap 还会影响实际点数。具体总点数预算仍需 Phase 1 验证。
  代码检查和绑定耗时均不能证明真实 GPU 渲染没有瓶颈。

## 2026-10-18 长记录真实回溯样本

日志文件：`C:\Users\23998\AppData\Local\BrainPlatform\logs\desktop-20261018.log`

记录：`8c7d2e447c6c4b4e9c8a24a250000001`，回溯会话：
`67451daddcd04921ad18239f140a8c19`。

- 共采集 `137` 条 `review_perf` 样本，未采集到 `algorithm_window_perf` 样本。
- `RawRead/Disk`：27 条，平均约 `3.6 ms`，最大 `12.1 ms`。
- `ChartBinding/PublishedFrame`：36 条，平均约 `1.1 ms`，最大 `13.6 ms`。
- `FrameBuild/FilterService`：14 条，平均约 `694.8 ms`，最大 `2290.5 ms`。
- 冷跳转出现在约 `9780 s`、`35330 s`、`53840 s` 和 `73990 s` 附近。
- 图表绑定点数保持在 `5000` 或 `7500` 左右，没有证据表明本次运行存在
  SciChart 数据序列无界增长。
- 冷跳转期间反复出现滤波会话创建、checkpoint、warmup、batch 和删除；这
  说明滤波源准备存在重复的会话/历史编排，但旧日志中的 `FilteredSource`
  耗时仍是 `0.0`，因此无法把 `FrameBuild` 的全部耗时精确归因到某一个 HTTP
  子步骤。

这组证据确认当前长记录延迟主要发生在帧构建及其滤波源准备链路，不能归因于
磁盘读取或 SciChart 绑定。它不等价于“全量算法结果已被复用”，因为本次没有
算法来源诊断样本。

## 新增对照运行：分块耗时已生效

最新回溯会话：`ae8c060b307e4aceb872955a333cb696`，时间约为
19:24:29--19:25:34。

- `FilteredSource/FilterService`：69 条，平均 `367.8 ms`，P95 `600.2 ms`，
  最大 `1522.3 ms`。
- `FilteredSource/FilterCache`：15 条，平均 `3.8 ms`，最大 `9.9 ms`。
- `FrameBuild/FilterService`：27 条，平均 `887.5 ms`，P95 `1375.7 ms`，
  最大 `2085.2 ms`。
- 同一会话的 `RawRead/Disk` 平均 `2.5 ms`，`ChartBinding` 平均 `0.6 ms`。

在同一时间范围内，滤波 HTTP 请求约为：69 次 session 创建、69 次 batch、
23 次 warmup、138 次 checkpoint 读取、69 次 session 删除。单个 HTTP 请求通常
只有几毫秒到几十毫秒，但一个滤波源块的端到端耗时可达数百毫秒到 1.5 秒，
说明延迟不只是网络传输，还包含本地 checkpoint 历史准备、锚点缓存查找/写入
以及多个串行步骤。

本机锚点缓存当时约有 2849 个 `.bpra` 文件、15.6 MB、120 个分组。代码中的
每次锚点写入都会触发全量目录的边界检查；这是下一步需要单独计时和评估的高
可信候选瓶颈，但目前还不能仅凭这次日志断言它是唯一根因。

这次运行仍然没有 `algorithm_window_perf`，所以全量分析结果复用问题仍未完成
验证。

## 锚点缓存索引优化后的对照结果

最新回溯会话：`eb9adf2de9734f9ab015c31a07d5c8a6`。

与上一轮相同记录的长跳转样本相比，优化后的阶段耗时明显下降：

| 阶段 | 优化前 | 优化后 |
| --- | ---: | ---: |
| `FilteredSource/FilterService` 平均 | `367.8 ms` | `54.9 ms` |
| `FilteredSource/FilterService` 最大 | `1522.3 ms` | `405.6 ms` |
| `FrameBuild/FilterService` 平均 | `887.5 ms` | `117.9 ms` |
| `FrameBuild/FilterService` 最大 | `2085.2 ms` | `452.8 ms` |

优化后 `FilteredSource/FilterCache` 平均仍约 `3.5 ms`，原始读取平均约
`2.9 ms`，SciChart 绑定平均约 `0.3 ms`。这支持缓存索引改动降低了
滤波源准备和帧构建延迟的判断，本次采样也未见磁盘或图表绑定成本明显升高。

最新会话的 HTTP 记录包含 145 次 checkpoint 写入、145 次读取、145 次批处理会话
创建和删除，以及 33 次 warmup。请求数量本身仍然偏多，但单请求延迟已明显
收敛；后续优化重点应从“缓存目录全量扫描”转向“多个连续滤波块是否可以共享
checkpoint 会话和历史推进”。

这仍不是严格的同一游标序列 A/B 基准，因此报告只把它记为强证据而不是最终
基准结论。此前的算法来源诊断仍为 `0` 条，完整分析结果复用在后续独立测试中
已得到验证，见下一节。

## 全量结果复用验收

同一日志文件中，PSD 在“按需预览 + 后台全量”模式下完成后台全量 Run 后，
拖动游标产生了 `23` 条有效窗口来源记录：

```text
source=FullRunStructuredCache
state=ready
run_id=a7247173ed744ae5b8a5dee95ec0b7d2
```

命中的游标范围包括 `18000`、`40000`、`52000`、`62000`、`85000`、
`99000`、`110000` 和 `123000` 等多个位置。读取耗时大多数为 `0 ms`，最大
为 `4 ms`，且没有为这些窗口重新提交按需算法计算。

这证明当前 PSD 动态查看已经能够复用已完成全量 Run 的结构化窗口缓存。此前
全量完成前产生的 `source=OnDemand` 记录属于旧阶段请求，不影响完成后的缓存
命中结论。该证据证明来源选择和读取路径，不替代全量与按需结果的数值一致性
验证。

同时新增了 WPF 回归测试：当结构化全量 artifact 和 indexed window 都不可用时，
动态查看保持当前预览，并明确回退到按需窗口；测试确认不会绑定空的或错误的
全量结果。该测试覆盖结构化和索引接口均抛异常时的回退；适配器另有空窗口列表
和缺少 `end_sample` 的测试。不能据此宣称过期、不兼容、部分 artifact 的适配器
边界及组合执行回退均已覆盖，这些场景仍需继续补齐。

## 34 分钟实测与 22 分钟连续播放

证据 E-03：同一 24 小时 fixture，回溯会话
`e0c3fee885bd43d082ff79de2e9e389c`，观察范围为本地时间
`2026-10-18 20:39:28.509--21:13:33.300`，共 `2044.791 s`。
日志为上述本机日志的限定区间，不提交完整日志或信号数据。
本机环境为 Windows 11 专业版 `10.0.26300`、Intel Core Ultra 7 255H、
物理内存约 `31.4 GiB`；开发 SDK 为 `.NET 11.0.100-rc.2.26504.105`，
应用目标为 `net10.0-windows`。基线提交仍为 `748044f` 加本地未提交增量；
这份旧日志未携带程序集构建身份，不能精确反推其二进制校验和。

| 阶段/来源 | 样本数 | 平均 ms | P95 ms | 最大 ms |
| --- | ---: | ---: | ---: | ---: |
| 原始读取 | 222 | 1.887 | 6.9 | 14.7 |
| 滤波源/缓存 | 445 | 6.053 | 15.7 | 60.6 |
| 滤波源/服务 | 222 | 46.064 | 61.9 | 365.7 |
| 帧构建 | 109 | 27.984 | 61.2 | 411.4 |
| 图表绑定 | 450 | 0.280 | 0.4 | 12.9 |

`20:51:33.304--21:13:33.298` 共有 67 次帧构建，墙钟推进约 `1320 s`，
帧起点也推进 `1320 s`，与连续 1 倍速播放相符。前 10 分钟
(`20:52--21:02`) 帧构建平均 `26.71 ms`，后段 (`21:03--21:13:34`)
平均 `21.79 ms`；本次没有呈现随播放时间持续变慢的趋势。
这是按数据准备边界采样的播放证据，不是每一帧都流畅的证明。

每条轨迹绑定观测峰值为 `7500` 点，未观察到点数随播放时间累积。
`FrameBuild points=0` 是现有诊断占位值，不是“没有波形”。
区间内未见 WARN、ERROR 或 HTTP 4xx/5xx，共 `1113` 次 HTTP 请求：
滤波 session 创建、checkpoint 写入、batch、checkpoint 读取、session 删除
各 `222` 次，另有 `2` 次 warmup 和 `1` 次 contract 读取。
这些 HTTP 计数按墙钟区间归集，不冒充单一会话的因果追踪。

结论 F-03（依据 E-03）：本次长记录连续播放的数据准备检查通过，未复现
“越播放越慢”，但内存、Dispatcher 延迟和 GPU 帧率在旧日志中均不可验证。
本次没有 `algorithm_window_perf`，不扩大为新的算法或全量复用验收。

后续路径 P-03：保留现有滤波数学与历史数据；补齐低频资源观测，之后再按证据
判断连续滤波块会话复用的收益。222 块产生 1113 次请求表明存在可优化的编排，
并不单独证明它已造成交互卡顿。Phase 2 多分辨率存储仍未开始。

## 可重复汇总与资源观测

从项目根目录复现 E-03 汇总：

```powershell
backend/.venv/Scripts/python.exe desktop-client/scripts/summarize_review_performance.py `
  "$env:LOCALAPPDATA/BrainPlatform/logs/desktop-20261018.log" `
  --session e0c3fee885bd43d082ff79de2e9e389c
```

省略 `--session` 时按日志时间戳选最新回溯会话，不按 Recording 位置、记录时长
或文件中出现顺序选取。JSON 包含阶段/来源统计、HTTP 区间计数、错误计数和
按进程分组的资源首值、末值、峰值与差值；没有资源样本时明确返回空集合。

新版本在已有 `BRAIN_REVIEW_PERF_DIAGNOSTICS=1` 开关下，为打开的回溯页面
每 10 秒采集一次 `review_resource_perf`，页面卸载即取消等待并停止定时器。
资源读取和日志写入不放在 UI 探针内；最多一个探针等待 Dispatcher，重复采样
会跳过而不是排队。采样不强制 GC，不读取信号值，不改变原始记录。

- `working_set_bytes`、`private_bytes` 为整个 WPF 进程资源，不是此页面独占内存。
- `managed_bytes` 是不强制回收的托管内存估计；`gc_heap_bytes` 是上次 GC 时的堆量。
- `gen0/1/2` 是累计 GC 次数，不是停顿时长。
- `dispatcher_queue_ms` 是 Background 优先级探针从入队到执行的等待时间，
  不是渲染帧率、GPU 用时或 Dispatcher 内所有工作的累计 CPU 用时。
- 资源值在探针入队前采集；若 UI 长时间阻塞，该值早于日志落盘时间。
- 一次内存升高不能单独认定泄漏，缺少旧数据也不能反向证明内存稳定。

自动化覆盖：探针去重、释放后不再读上下文或发布、Dispatcher 关闭、无当前记录、
采样异常隔离、真实进程字段、汇总会话选择、缓存/服务耗时分离、请求归集和
多进程内存分组。真实页面的新增资源曲线仍待运行采集，不能用这些测试替代。

## 用户日志提供的事实

用户在长记录中观察到同一个 review 会话附近反复出现：

```text
POST /api/live-filters/sessions/{review-session}/warmup/binary?sample_count=2500
GET  /api/live-filters/sessions/{review-session}/checkpoint
```

这证明交互过程中发生了重复的滤波 warmup/checkpoint 请求，但仅凭 HTTP
日志还不能判断每次请求是否真正重新计算了相同窗口，也不能判断请求来自
哪个客户端回溯会话。新增的 `review_session_id` 和阶段来源字段用于完成这
个关联，不能把接口名称直接当作“重新计算”的证据。

## 尚未证实

- 10 秒、10 分钟、1 小时、24 小时和多日记录的实际性能矩阵。
- SciChart GPU 渲染、Dispatcher 耗时和 GC/内存曲线。
- 算法 `window-preview` 的完整结果查找/计算来源，以及全量结果是否被重复
  计算。现在已经有来源诊断，但尚未在真实长记录上采集对照矩阵。
- 真实设备或用户机器上的多日回溯体验；自动化测试不能替代该验收。

## 根因决策

当前不能仅凭“`window-preview` 请求很多”或“warmup 请求很多”就决定实施
多分辨率索引。现有样本未显示磁盘读取和图表绑定是主要耗时，锚点索引优化后的
滤波源准备明显改善，连续播放也已有独立证据。Phase 0 仍缺完整时长矩阵、
资源曲线与 Dispatcher 证据。前台过期响应发布已由后续的
[专项自动化测试](2026-10-18-review-publication-fencing.md)覆盖，后续增量也已覆盖
检查点进度通知隔离与关闭行为。新增资源观测应随后续正常验收采集，
不要求用户为了已通过的 22 分钟播放检查原样重复测试。

在根因判定前，保留现有有界回溯路径，不修改滤波相位/边界语义，不引入
“拖慢游标”或未经证据支持的多分辨率存储层。

## 下一步

1. 后续正常运行时采集新增资源诊断；区分缓存命中与服务耗时，并继续定位
   checkpoint 历史准备、目标滤波和缓存持久化的子步骤成本。
2. 采集上述五档时长矩阵和至少一个多日 fixture 的自动化读盘证据。
3. 分别记录 SciChart 绑定耗时、点数峰值、挂起请求数量和进程工作集。
4. 全量 artifact 回退测试已补齐，后续运行可观察其显式回退来源。
5. Phase 1 已依据确定性回归暴露的前台发布缺陷实施有界修复，见上述专项报告；
   不据此推断新的性能根因，Phase 2 多分辨率显示仍需独立证据再实施。

本记录不是临床或医学有效性结论；它只描述工程性能证据和当前未知项。

## 资源诊断轮自动化验证

- WPF 全套：`735/735` 通过，包含 7 个新增资源采样测试。
- 后端定向：`16/16` 通过，包含 5 个日志汇总测试及现有滤波服务、API、
  checkpoint 缓存和滤波契约回归；本轮没有运行后端全套。
- 隔离 Release 构建通过：
  `dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --artifacts-path .artifacts/review-resource-validation`。
- `openspec validate improve-long-recording-review-performance --strict` 通过。
- `git diff --check` 通过。
- 已有工具链警告仍在：预览版 .NET SDK、OpenTK/SkiaSharp 的 `NU1701`，以及
  Starlette/httpx/anyio 弃用提示；不把它们描述为新增测试失败。
- 补充规模检查未通过：`backend/app/services/spectral_analysis.py` 为 533 行，
  超过其登记的 500 行；`backend/app/persistence/repositories/dynamic_attempt.py`
  为 478 行但缺少评估登记。两份文件相对 `HEAD` 均无修改，属已有缺口，
  本轮不扩大范围重构或放宽策略。新增诊断、测试和汇总脚本均未超过 400 行。

本轮只补齐诊断、汇总工具与证据记录，没有改变算法公式、滤波语义、导航策略，
也没有操作真实设备或原始记录。新增探针在真实页面上的曲线尚未采集，
相关 OpenSpec 验收项保留未完成。

## 全量结果完整性与回退验证

本轮是传输完整性与失败恢复验证，不是算法数值重验证或新性能验收。
沿用 `brain-research-platform` 的结果来源、空值与预热状态约束，没有改动
EEG 公式、滤波数学、原始记录、设备或已有全量任务。

E-04：新增 `FullResultFallbackTests`，修复前首次运行 17 项中 15 项失败。
测试揭示缓存 Run/文件所属 Run/科学版本未核对、缺失数组行被当作整块输出、
无效结构化缓存没有继续尝试索引读取等缺口。后续加入空窗口与真实后端无显式
窗口 ID 的兼容用例；修正测试桩内与所选算法版本不一致的预览版本，未放宽
生产校验来迁就测试。

F-04 / P-04：在共享 `FullRunPreviewIntegrity` 校验 Run 与所属请求，以及结构化
缓存的 Run、版本、窗口顺序、范围、可选显式 ID 和数组几何。窗口 ID 缺失时
沿用现有后端/适配器的数组行号身份。缓存只在加载时进行一次全量结构校验，
不在每次游标更新时扫描全部数值；失败后先试索引，再显式回退按需。
索引响应另核对 Run、记录、游标、版本及非未来窗口范围。校验失败的缓存不再
触发本地命中专用的即时拖拽刷新，避免绕过按需节流。科学身份/校验和仍由
后端负责，前端没有重新实现算法身份计算或 EEG 运算。

E-05：`test_window_artifact_fallback.py` 在临时目录实际修改索引或派生 NPZ，
通过真实结果路由验证 409 结构化错误；该测试用轻量 service 桥接实际
ArtifactStore，不冒充完整 Run 队列端到端。覆盖索引缺失、截断、非对象、过期
SHA、未知格式、行号错配，以及窗口/坐标文件缺失与校验失败。每例还检查
canonical NPZ 原字节不变且可读；另有独立坐标读取校验和 canonical 文件缺失测试。

F-05 / P-05：ArtifactStore 窗口与坐标读取共用索引身份验证，并将缺失/不可读
派生文件、损坏索引、坏 NPZ 转为已有结构化 unavailable/integrity 错误。
没有删除或重写历史 Run、canonical 文件，也没有在读取接口触发计算。

E-06 / F-06 / P-06：`FullRunWindowIntegrityTests` 验证 PSD/STFT 结构、合法
`Partial` 保留预热来源与数值、拒绝窗口不跳过到旧成功值。结构化适配器现与
后端索引读取保持一致：可用预热窗口可显示，不可用结果为 null 并保留原因，
不能把“科学 Partial”混同于“只写了一部分的损坏文件”。

验证命令与结果：

- WPF 全套：`dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --verbosity quiet`，764 项通过，本轮新增 29 项。
- 后端定向：124 项通过，覆盖新增 artifact 回退、windowed artifacts、结果导出、
  窗口索引、离线复用、Run foundation/API/queue/controls、溯源、官方算法 Runs、
  日志汇总与现有滤波服务/API/checkpoint/契约回归；未运行后端全套。
- 隔离 Release 构建、当前 change 的 OpenSpec strict 与 `git diff --check` 通过。
- 规模检查仍只有前节记录的两处既有后端缺口；本轮生产与测试新增文件均小于
  400 行，`AlgorithmListViewModel.OnDemand.cs` 保持 399 行，未机械拆分或放宽上限。

未新增真实 GUI/GPU、资源曲线或多日性能证据，不要求重复已经通过的人工
播放检查；后续仍需补 Phase 0 缺失资源证据。Phase 1 前台过期响应隔离测试
现已完成，结果与其未覆盖边界见[专项报告](2026-10-18-review-publication-fencing.md)。

## 显示级别 chunk 存储增量

本次实现多分辨率显示层的可丢弃存储、纯生成基础以及可选的 SciChart 命中读取。
`ReviewDisplayLevelChunkCache` 使用版本化 magic、显示级别
指纹、chunk 与通道索引，写入临时文件后原子替换；损坏、截断、身份不匹配的
条目一律视为 cache miss。`ReviewDisplayLevelChunkGenerator` 从已经投影的
V/float64 显示段生成按时间顺序的 min/max bucket；缺段、bucket 内覆盖不完整
或非有限值会产生显式 gap，不把 gap 两侧的 extrema 合并。

定向验证：显示级别缓存、索引、生成器、读取器、坐标适配器、发布器、身份生成器、
选择器和 provider 共 24 项通过；Release 构建、OpenSpec strict 和
`git diff --check` 通过。显示 chunk 二进制格式已升级以保存 extrema 样本计数。
`ReviewDisplayLevelProvider` 只有在完整 index/chunk 命中时才替换 SciChart 点数组，
缺失或不连续时回退现有 bounded frame path；filtered level 生成、
overview/detail 读盘和真实资源曲线仍未验收。
