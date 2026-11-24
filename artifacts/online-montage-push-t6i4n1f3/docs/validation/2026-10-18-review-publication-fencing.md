# 回溯前台请求发布隔离验证

## 结论与范围

本轮修复前台回溯请求的发布竞态，不是长记录性能最终验收。拖拽接受新目标时，
旧请求立即失效；旧请求稍后成功、失败或结束清理，都不能覆盖新目标的帧与加载状态。
连续播放仍复用正在读取的数据块，不因每次播放时钟更新而重启加载。

范围为 `RecordingReviewSession`、ViewModel 导航入口及其确定性回归测试。
基线为 `748044fab361abbe75923b807c480043be6336c5` 加当前未提交工作区；
测试运行于本机 Windows、目标 `net10.0-windows`。没有操作真实设备或原始记录，
没有改动算法公式、滤波数学、参考映射、样本计数、单位或科学窗口定义。

## 证据、发现与修复路径

### E-01：先复现再修复

新增测试第一次执行为 16 项、12 项失败、4 项通过。该红灯记录来自本轮修复前，
当前工作区已无法直接复现红灯，不为演示而回退用户文件。

`ControlledReviewReader` 使用内存合成窗口、20 Hz 和非零计数起点 1000，故意忽略
取消信号，允许测试精确控制旧请求晚到的成功或异常。测试不靠实际磁盘延迟或睡眠
凑竞态；每个时间范围使用可核对的源值标记，并保留 V 与真实样本计数对应关系。

F-01：轨道点击的旧操作在等待返回后重新读取已被其他请求释放的取消源，可能
抛出 `ObjectDisposedException`；过期读取的普通异常也可能逃逸。来源为 E-01
及生产代码中原来的 `previousLoad.Dispose()` / 等待后的 `load.Token`。

P-01：每个操作持有自己的 token 并在自身 `finally` 释放取消源；替代请求只取消，
不释放另一个操作持有的源。旧版本的成功、异常及清理路径均不得发布当前状态；
当前活动请求失败仍显示错误，不能用捕获旧异常的规则隐藏真实失败。

F-02：ViewModel 节流器已接受新拖拽目标，但替代加载尚未开始时，旧请求仍可能
发布帧。来源为 E-01 与 `PreviewSeek` 原先仅尝试缓存命中的路径。

P-02：`PreviewSeek` 调用 `PreviewNavigationTarget`，在进入节流队列前使前一个
前台请求失效。正常播放的缓存探测仍使用原入口，避免每个时钟 tick 都取消工作。
会话释放先使前台版本失效，再取消生命周期，晚到结果不得发出 Changed 通知。

### E-02：17 项定向回归通过

从项目根目录执行：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj `
  --filter 'FullyQualifiedName~RecordingReviewPublicationTests|FullyQualifiedName~RecordingReviewQueuedTargetTests' `
  --no-restore --verbosity quiet
```

结果：17 项通过，0 失败、0 跳过。测试分别覆盖：

| 边界 | 用例数 | 核对内容 |
| --- | ---: | --- |
| 旧操作先于新操作结束，成功/失败、轨道/普通加载 | 4 | 不能替换帧或清除新加载状态 |
| 新操作先完成，旧操作随后成功/失败 | 4 | 帧、状态、位置不回退，无旧 Changed |
| 释放后旧操作成功/失败 | 4 | 无晚到通知，保留完整帧，取消已传递 |
| 缓存返回替代挂起轨道点击 | 1 | 旧点击不能晚到提交 |
| 当前活动请求失败 | 2 | 真实错误仍可见，原完整帧保留 |
| 拖拽目标仍在节流器中等待 | 1 | 旧帧不发布，新帧只发布一次 |
| 冷数据块等待期间的连续播放 | 1 | 100 次 tick 后只有一次延迟读取且未取消 |

F-03（依据 E-02）：上述前台路径满足版本隔离。新帧核对 Recording 身份、
绝对范围、源样本计数及 V 源值；旧完整帧不被改写成新目标的时间坐标。
这证明会话和 ViewModel 的发布边界，不等于真实 SciChart 已完成 GPU 绘制。

P-03：用户目标先失效旧前台版本，再命中缓存或进入有界节流；完整新帧通过身份
检查后发布。播放时钟独立推进，等待中的一个数据块可继续完成并复用。

### E-03：完整回归与交付检查

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj `
  --no-restore --verbosity quiet
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj `
  -c Release --artifacts-path .artifacts/review-resource-validation --verbosity quiet
openspec validate improve-long-recording-review-performance --strict
git diff --check
```

- WPF 全套 781 项通过，0 失败、0 跳过；本轮比上轮 764 项增加 17 项。
- 隔离 Release 构建成功，0 错误；OpenSpec strict 与 diff 检查通过。
- 已有 OpenTK/SkiaSharp `NU1701` 和测试 `xUnit2013` 警告仍在，未被当作新失败。
- 本轮没有新增后端改动或重跑后端全套；上轮定向 124 项结果见
  [Phase 0 报告](2026-10-18-long-recording-review-phase0.md)。
- 会话最终 705 行，已在 `docs/code-size-policy.json` 作高风险登记，上限 715 行。
  后续增加功能前，应把源块加载、检查点协调与块内共享任务所有权抽成独立加载器，
  保留会话作为单一前台版本和帧发布所有者。既有两处后端规模缺口未借机放宽。

## 尚未覆盖与下一步

1. 首轮尚未覆盖的检查点进度与释放边界，已由下面的后续增量补齐测试和修复。
   这不是对所有异步路径无竞态的普遍证明。
2. 节流器仍串行等待 action；不合作的底层读取可能延迟下个加载。旧发布安全不等于
   任意存储和滤波条件下都能保证固定响应时延。
3. 不验证真实 GPU、每帧流畅度、进程内存/Dispatcher 曲线或多日性能矩阵。
   已通过的人工 22 分钟连续播放不要求原样重测；新增观测随正常运行采集。
4. 多分辨率显示层、全路径内存预算与最终 OpenSpec 归档尚未完成，不能把本轮
   781 项通过当作整个长记录专项已结束。

本报告按 `docs-generator` 的任务型文档与证据/发现/路径组织，只保留复现命令、
结论与边界，不嵌入 EEG 数组、完整用户日志或无关安全报告章节。

## 后续增量：检查点进度隔离与源加载器拆分

### E-04：取消与关闭缺陷复现

`ReviewCheckpointLifetimeTests` 在生产修复前首次可执行运行共 9 项，6 项失败、
3 项通过；先前一次编译因测试枚举可访问性失败，不计作行为红灯。
测试分别在创建会话、历史推进、目标滤波、最终 checkpoint 导出时挂起一个
忽略取消的步骤，之后放行成功或 IOException，不使用固定睡眠制造竞态。
另测一个执行中任务与一个排队任务同时遇到关闭。

F-04（依据 E-04 与原 `ReviewCheckpointCoordinator.DisposeAsync/Publish`）：
取消后的失败仍能走全局进度事件；关闭只释放 semaphore，未取消已接纳任务，
晚到任务随后释放已销毁的 semaphore 存在异常风险。

P-04：协调器为每个已接纳操作建立 linked token 并计数，排队操作也计入。
关闭先拒绝新操作、取消存量并抑制后续发布，不等待不合作的远端步骤；最后一个
操作退出并释放 gate 后，才释放同步资源和生命周期 token。取消后的返回值在
检查点边界被检查，当前活动操作的真实失败仍正常暴露。

### E-05：共享任务与前台状态分离

新 `ReviewSourceChunkWorkTests` 共 4 项，覆盖独立订阅者取消、另一订阅者继续
复用同一任务、纯预取被前台接管时回放最新进度，以及源生命周期取消后的晚到通知。
进度回放以序号避免同一订阅者收到倒退状态，回调不在共享任务锁内执行。

`RecordingReviewProgressTests` 共 6 项，白盒取得实际的前台进度回调，验证
新冷目标/缓存返回不能被旧进度或错误覆盖，以及排进 SynchronizationContext 的
回调在任务完成/会话关闭后重新检查身份。这是请求归属边界测试，不冒充 GPU 测试。

`RecordingReviewHttpProgressTests` 共 3 项，调用实际 HttpRecordingReviewFilter
与源加载器。HTTP handler 只返回合成契约、checkpoint 和恒等二进制响应，
不启动 Python、不重现滤波公式。验证当前冷目标可见 PreparingHistory、返回
缓存后旧请求成功/HTTP 500 均不再发出前台 Changed，以及纯预取不改变前台状态。
测试等待实际共享任务完成；超时不能被当作允许的旧请求异常而忽略。

F-05（依据 E-05 与原会话全局进度订阅）：旧方案的共享源块生命周期长于前台
等待者，单一全局进度事件无法区分旧目标、当前目标或纯预取。

P-05：源块拥有进度，等待者按需订阅并在退出时移除。会话回调捕获其前台版本与
token；跨线程进度投递回原 SynchronizationContext，执行时再次校验版本、
取消、释放和 IsLoading，已完成帧不被迟到进度改写。新目标重置旧准备状态。
原有同块复用和 100 ms 无订阅者宽限保留，不为每个目标重启科学计算。

拆分后的职责与物理行数：

| 文件 | 行数 | 职责 |
| --- | ---: | --- |
| RecordingReviewSession.cs | 450 | 前台版本、取消、帧与缓存激活、进度发布 |
| ReviewSourceLoader.cs | 268 | 原始/滤波源读取、10 秒块、检查点协调 |
| ReviewSourceChunkWork.cs | 108 | 同块任务所有权、订阅与取消宽限 |
| ReviewCheckpointCoordinator.cs | 282 | 精确历史推进与操作生命周期 |

会话从修复前 705 行缩到 450 行；原先拆分待办已兑现，规模登记降到 455 行。
没有为凑行数复制状态；前台 revision 仍只有一个所有者。源块科学缓存 key、
滤波路径、原始窗口/帧 LRU 容量、样本与单位保持不变。

### E-06：后续增量验证

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj `
  --filter 'FullyQualifiedName~ReviewCheckpointLifetimeTests|FullyQualifiedName~ReviewSourceChunkWorkTests|FullyQualifiedName~RecordingReviewProgressTests|FullyQualifiedName~RecordingReviewHttpProgressTests' `
  --no-restore --verbosity quiet
```

定向 22 项通过。完整 WPF、隔离 Release、OpenSpec strict 与 diff 命令沿用 E-03，
本次完整 WPF 为 803 项通过；本轮未运行后端全套。原有 checkpoint 锚点、gap、
缓存/预热、旧响应、连续播放与导联测试仍在完整回归中。
已有 NU1701/xUnit2013 警告保留，新增代码未留下新的分析器警告。

剩余边界：即使页面已关闭，不合作的远端步骤仍可能暂时占据资源；本轮保证它
不能改写当前 UI，也不因提前释放 gate 引发清理异常，不声称能强杀远端计算。
仍需独立完成进程/Dispatcher 曲线、多时长矩阵和多分辨率显示。

### E-07：当前路径资源预算

本增量为原始/帧内存缓存和滤波源块准入增加了可执行上限。每类缓存仍保留
原有条目数上限（raw 2、frame 3），并额外按实际 `double` 数组长度计算 64 MiB
payload 上限；超大的完整窗口不会被截断，只返回当前调用者、不进入 LRU。源加载器
每个 review session 最多接纳 2 个未完成的过滤源块构建；第三个构建可取消地等待，
相同 fingerprint 仍复用现有任务，取消订阅者不会提前释放仍在执行的准入名额。

测试证据：`RecordingReviewWindowCacheTests` 3 项覆盖 raw/frame 独立预算、LRU 淘汰、
超预算不缓存和原数组完整性；`ReviewSourceLoaderBudgetTests` 1 项在两个构建阻塞时
确认第三个没有启动，释放后才完成。定向测试共 4/4 通过。该预算不等同于进程总内存、
GC、SciChart/GPU 或非合作远端调用的硬上限；这些仍需实际日志和人工矩阵验证。

### E-08：自动化验证收口

本轮执行结果：WPF 全套 `807/807` 通过；后端 `766/766` 通过；独立 Release 构建成功；
OpenSpec strict 校验成功；`git diff --check` 成功。保留既有 NU1701、xUnit2013、
FastAPI/Starlette 弃用警告，不把警告误记为本轮缺陷。自动化结果只证明代码契约、预算
准入和回归路径，不能证明真实 SciChart/GPU 帧率、进程内存曲线或多日数据接受标准。

### E-09：波形与算法刷新隔离

代码审计确认 `RecordingReviewSciChartCanvas.ApplyFrame` 以帧引用、绘图区尺寸、
灵敏度和屏幕比例作为重建条件；同一完整帧在状态通知或游标更新时不会重新生成
trace 数组。PSD 图表是独立控件，不由波形控件的属性通知驱动。`ReviewWaveformRefreshTests`
验证重复应用同一帧不触发 SciChart 数据系列变化，Revision 改变才重建。

交互回归还覆盖正反向拖拽、快速目标替换、播放、冷定位、缓存定位、滤波变更和
录制 gap；这些是确定性契约测试，不等价于真实 GPU 帧率或窗口人工验收。

### E-10：请求与资源观测管道

`summarize_review_performance.py` 已按标准化路由、HTTP 方法和状态聚合请求，
并将 `review_perf` 的计算来源与 `review_resource_perf` 的进程样本分开；
`test_review_performance_report.py` 覆盖请求计数、会话区间和多进程资源分组。
WPF 资源探针为可选的 10 秒采样，关闭竞态不会把探针异常传播到回放页面。
现有真实日志没有有效 `review_resource_perf` 样本，因此真实内存/Dispatcher 曲线
仍是未验证状态，不把观测管道测试当成性能验收。

### E-11：显示粒度选择契约

新增 `ReviewDisplayLevelSelector` 作为 Phase 2 的纯契约层：以绝对可见时长、采样率、
绘图区像素宽度、通道数和源 fingerprint 生成显示级别；目标点数为每像素最多两个
极值点且总量不超过 2,000，桶大小取满足预算的最小二次幂。5 项测试覆盖短/长视窗、
源与通道身份隔离以及非法几何。当前尚未接入 SciChart 或磁盘索引，因此没有改变
现有波形数据和科学结果。

该契约加入后完整 WPF 回归为 `812/812` 通过；既有依赖兼容性警告仍保留。
