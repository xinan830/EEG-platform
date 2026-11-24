# 回放自定义频率业务交付记录

日期：2026-11-06。OpenSpec：`add-review-custom-frequency-inspection`。

## 交付范围

本次完成后端科学投影、桌面类型化 API、面板命令、ViewModel 与回放生命周期。用户后续要求补齐初始页面，已增加 XAML、双栏布局、通道表格和 SciChart 频率游标控件。当前交付状态见文末“归档发布检查”；完成界面或归档不代表已完成真实录音的界面/性能验收。

原五频段数值面板与 PSD/RBP 地形图入口保留。新面板复用已有 PSD/RBP 回放窗口绑定及通道、预处理、分析窗口和 Welch 分段设置，不新增算法 ID 或独立 EEG/Welch 计算线路。

## 科学来源与接口

| 接口 | 请求 | 响应职责 |
| --- | --- | --- |
| `POST /api/analysis-windows/custom-frequency/bins` | `psd_window_id`、`rbp_window_id`、可选 `project_id` | 当前匹配窗口的实际频率轴、bin 边界、逐通道所有 bin 指标和来源 |
| `POST /api/analysis-windows/custom-frequency/range` | 同上，增加 `low_hz`、`high_hz` | 指定范围的逐通道指标、精确边界和来源 |

两个接口只读保存的窗口产物，不进入 raw EEG、Welch 或地形图插值/空间坐标准备。窗口配对核对录制 ID、source SHA、半开样本范围 `[start_sample, end_sample)`、采样率、参考、滤波、Welch/窗口策略设置及导联顺序。可选域是 PSD/RBP 在 1–50 Hz 内共同支持的实际频率网格；交集内网格不一致则拒绝。

PSD 密度读取 PSD 结果；绝对/相对功率读取 RBP 所属的保存频谱投影。RBP 在既有一次 Welch 后保留 `frequency_hz` 和 `spectral_psd`（`V^2/Hz`），不改变五频段数组、full-series 形状或地形图字段。当前身份：

- 科学版本：`official-rbp-v4`。
- 实现：`rbp-runtime-v9-private-projection-source`。
- Definition：`2.0.9`；按需窗口策略：`rbp-dynamic-window-v4-custom-spectrum`。

Definition 按不可变内容身份安装，不覆盖旧 Definition；历史 Run/窗口不迁移、不改写。没有谱源的历史产物不能用于新投影，原有输出仍按其原契约读取。开发阶段使用过 `2.0.7`/`2.0.8`；本次性能边界修复发布新的 `2.0.9`，科学版本和公式不变。API 测试以独立 `BRAIN_STORAGE_ROOT` 运行，未删除或覆盖旧记录。

### 单位、积分及质量

| 指标 | 来源与含义 | API / 桌面单位 |
| --- | --- | --- |
| 单点 PSD | PSD 结果实际频点的密度 | `µV²/Hz` |
| 范围 PSD | PSD 结果在范围内积分后除以完整范围宽度 | `µV²/Hz` |
| 绝对功率 | RBP 保存频谱的精确边界线性插值 + 梯形积分 | `µV²` |
| 相对功率 | 上述 RBP 功率 / 保存的完整五频段 1–50 Hz 总功率 × 100 | `%`，桌面不得再乘 100 |
| 等效 RMS 振幅 | 后端 `sqrt(RBP 绝对功率)` | `µV` |

单 bin 的积分区间以相邻实际频点的中点分界，首末边缘限制在共同支持域内。因此功率复用 RBP 积分政策，谱形变化时不保证等于中心 PSD × bin 宽度；不能拿用户示意表验证这条等式。PSD 和 RBP 的质量保留政策也可能不同，所以应保留两项来源质量，不能把 RBP 数值解释成 PSD 路径的替代结果。

实测保存窗夹具使用 4 s Welch，网格为 1、1.25、1.5…50 Hz，共 197 点；内部 bin 宽 0.25 Hz，首 bin `[1, 1.125]`、末 bin `[49.875, 50]` 宽 0.125 Hz。完整 bin 功率和等于保存的 RBP 总功率，相对百分数和约为 100。`1–3`、`1.1–2.9`、`1–50` Hz 范围均覆盖。前端必须用响应轴和边界，不硬编码 0.25/0.5 Hz。

PSD/RBP 质量分别保留。不可用数值为 null，原因跟随来源；缺少/拒绝的通道不补零、不用上一窗替代。非法、反向、零宽、非有限或超出共同支持域的范围会被拒绝；单点越界不会静默裁剪成合法值。

## UI 绑定交接

入口文件：`desktop-client/BrainPlatform.Desktop/Modules/Review/Views/RecordingReviewView.xaml`。沿用现有面板 ContentControl / DataTemplate 接线：

1. 工具栏/菜单已新增“自定义频段”，绑定父 VM 的 `OpenCustomFrequencyInspectionCommand`。
2. `ReviewAnalysisPanelKind.CustomFrequencyInspection` 模板已接入 `ReviewCustomFrequencyInspectionPanel.xaml`。父数据上下文仍为 `RecordingReviewViewModel`；面板内部的数据上下文绑定 `CustomFrequencyInspection`。
3. 面板 `.xaml.cs` 只处理 Enter 提交数值输入；`ReviewCustomFrequencyChart.cs` 只处理一个 PSD 系列和频率注释交互，不调用 raw EEG、原 PSD 图表重建、Welch 或自行积分。
4. 已移除 `ReviewAlgorithmRegistrationTests.cs` 对 `CustomFrequencyInspection` 的临时枚举排除，恢复完整面板模板和双入口覆盖。

业务接线位于 `Modules/Review/ViewModels/RecordingReviewViewModel.CustomFrequency.cs`，面板数据状态位于 `Modules/Review/ViewModels/ReviewCustomFrequencyInspectionViewModel.cs`（均在桌面项目内）。

| 控件需求 | 可直接绑定的成员 |
| --- | --- |
| 单点/范围模式 | `Mode`：`ReviewCustomFrequencyMode.SingleFrequency` / `Range` |
| 单点游标双向 Hz | `SelectedFrequencyHz`，setter 吸附实际 bin；显示 `ActualFrequencyHz` |
| 实际轴及边界 | `FrequenciesHz`、`BinIntervals`、`SelectedBinIndex`、`MinimumFrequencyHz`、`MaximumFrequencyHz` |
| 双范围边界/输入 | `RangeLowHz`、`RangeHighHz`；状态 `IsRangeValid` |
| 逐通道数值表 | `Rows`；列为 `Channel`、`AmplitudeRmsUv`、`AbsolutePowerUv2`、`RelativePowerPercent`、`PsdDensityUv2PerHz` |
| 表格选中通道 | `SelectedValuePath=Channel`、`SelectedValue` 双向绑定 `SelectedChannel` |
| 当前单通道曲线/tooltip | `PsdCurve` 的 `FrequencyHz`、nullable `DensityUv2PerHz` |
| 不可用/质量/来源 | 行 `PsdQuality`、`RbpQuality`、`Details`、`RbpDenominatorUv2`；面板 `StatusText`、`WindowText`、`ProvenanceText`、`HasBins`、`IsFrequencyValid` |

数值列显示 null 为破折号，列标题显式带单位。`RelativePowerPercent` 已经是百分数，使用普通数值格式而非再次乘 100 的百分比格式。DataGrid 自带排序使用 CollectionView，不对 `Rows` 源集合排序；已启用横纵滚动、冻结通道列、单行选择、数值列排序和行级质量明细 tooltip。用户可继续调整当前 XAML 的视觉样式。

bin 投影每次匹配窗口加载一次；移动单频点和切换通道只本地选取后端值，不发请求。单点拖动不改变 `PsdCurve`，通道切换才更换该通道曲线数据。UI 保持同一 SciChart surface/series 实例并更新数据，不在游标或每行 PropertyChanged 时重建图表。单点模式一条可编辑频率线，范围模式两条可编辑边界和范围底色；输入在失去焦点或 Enter 时提交。X 轴固定显示 0–50 Hz，纵轴按当前通道数据留出 12% 余量，null 绘制为断点；hover 使用 SciChart CursorModifier 的 X 方向曲线吸附，关闭插值，横轴悬停标签通过当前数据序列查找最近实际 bin。

范围变动立即清空旧数值，180 ms 防抖后请求同一保存窗。取消令牌和窗口/选择 revision 共同隔离晚到响应；关闭面板、seek、换窗、改配置会失效旧值。投影异步进行，seek/tick 不等待它。

## 已运行验证

- 后端相关回归：**155 passed、10 deselected**。覆盖新服务/API、真实保存窗、单位/来源/边界/质量、原五频段、RMS、PSD 地形图、标量投影、按需输出、目录、official/combined 执行。使用隔离 storage。
- 补齐 UI 后完整受影响桌面 Review + 新 HTTP 测试：**386 passed、3 failed，总计 389**。3 项失败均在从 `HEAD` 独立提取的原始基线复现，见下表；不作为新功能通过项。
- 新增业务/HTTP 专项独立复跑：**19 passed、0 failed**；用例位于 `ReviewCustomFrequencyInspectionViewModelTests.cs`、`ReviewCustomFrequencyLifecycleTests.cs`、`HttpCustomFrequencyTests.cs`，覆盖吸附、排序源顺序、null/单位、错误合同、范围取消/防抖、晚到响应、关闭释放与不发重复科学请求。
- 新 UI 控件测试 2 项及完整模板/双入口注册测试通过；真实创建 WPF 窗口校验数值表绑定、28 通道排序/横纵滚动、选中曲线、单点吸附、双边界 drag 事件、范围输入、清空失效值、chart/series 同实例、卸载/重载订阅。编译 0 错误。
- 首次预览测试宿主未执行应用许可证初始化，SciChart 仅显示水印和轴标题；以应用现有许可证在临时预览 harness 初始化后，已目视确认曲线、刻度和单点游标正常绘制。许可证没有复制进新源码。自动化没有覆盖物理鼠标拖动/hover tooltip 的最终用户操作。
- 保存窗 API 测试在投影前封禁 raw spectrum loader、Welch、空间 provenance 和 topomap 插值；请求仍成功，保存窗与录制源 hash 保持不变。
- OpenSpec strict、差异空白检查及本次变更文件规模检查通过。全仓文件规模检查仍有 3 项既有基线失败，未扩大本次范围处理。

| 既有桌面失败 | 已确认情况 |
| --- | --- |
| `Provided_recording_exports_through_real_http_client_when_acceptance_is_configured` | 未配置真实 recording/output/backend，动态 skip 标记被当前 runner 计为失败 |
| `Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel` | 原始基线同样 Expected 1 / Actual 0 |
| `Dragging_exclusively_owns_position_then_restores_previous_playback_state(True)` | 原始基线同样 Expected false / Actual true |

初始 UI 阶段的全仓规模基线有三项。本次性能修复将 `on_demand_dynamic.py` 的 session/plan/status 映射移至 `dynamic_window_responses.py`，由 558 行降到 500 行并更新职责登记，解决其中一项。仍有两项无关失败：`algorithm_recording_context.py` 410 行未登记、`algorithms/psd/runner.py` 464 行超过登记 420。原 `test_official_algorithm_runs.py` 已有 549 行，本次只同步 Definition 断言，无代码增长。

额外探查的 IAPF/ThetaBeta scalar/combined 旧测试夹具使用 5 s 窗，而当前策略仅接受完整 20/30 s 窗，存在无关失败；相关最终回归用 `-k 'not iapf and not theta_beta'` 排除，未改它们的科学策略或夹具。构建仍有原有预览 SDK、OpenTK/SkiaSharp NU1701 警告，本次没有新增依赖。

## 保存源性能修复（2026-11-06）

### 修改边界

- RBP manifest 将 `frequency_hz`、`spectral_psd` 声明为后端 `projection_fields`。它们仍由原一次 Welch 产生并保存在同一不可变 NPZ 中；普通 ensure/status、标量、地形图读取在选择 NPZ 成员前排除这些字段，同时排除对应单位与形状。不是在 HTTP 最后删除已解码矩阵。
- 自定义接口显式选择内部源视图，在相同项目范围校验之后获取 NumPy 矩阵，避免先转换嵌套 list 再重建 NumPy。公开视图保持原 list 契约。
- 产物所有者持有最多 32 项/32 MiB 的 LRU 解码缓存（包含 NumPy buffer 计量），公开/内部视图分别记账。同一产物的并发冷读合并，返回深拷贝隔离调用者修改；关闭服务清空并禁止后续后台写入重新驻留。
- 每次读取仍计算 metadata/NPZ 内容 SHA-256 指纹并核对数据库 manifest 身份，内容或文件签名变化会失效缓存、重新执行完整校验；同大小且保留修改时间的损坏也被拒绝。缓存只省解压、模式验证及内部列表转换，不省略内容校验。原单窗展开预算、无 pickle、原子发布与目录预算保留。
- bin 积分改为每个区间一次对所有有效通道调用同一个 `band_power` 原语，RMS 仍调用既有 `band_rms_uv`。测试以非平坦谱和非均匀网格逐值对照原标量投影，容差 `1e-13`。不是新增算法或改变积分政策。
- 检查了 full-window reuse/publisher：RBP 未声明该复用能力，不会从 full-series 发布缺少频谱的当前窗口；PSD/STFT 原共享路径由回归覆盖。

### 合成测量

可复跑入口：`backend/tests/custom_frequency_benchmark.py`。使用临时合成 WPF 记录、200 Hz、10 s 分析窗、21 个 M2 参考导联、197 频点，各项 20 次；不使用用户录音。冷读每次清除解码缓存，操作系统页缓存不清除；热读沿用相同窗口。HTTP 为进程内 FastAPI TestClient，包含序列化/请求分派，**不代表真实网络、WPF 帧率或真实录音长时播放性能**。最后一轮结果：

| 路径 | 中位数 ms | P95 ms |
| --- | ---: | ---: |
| 普通 RBP 窗读取，冷 / 热 | 7.356 / 3.965 | 8.036 / 4.389 |
| bin 服务，冷 / 热 | 31.691 / 26.104 | 36.712 / 26.710 |
| 范围服务，冷 / 热 | 16.069 / 10.133 | 21.226 / 10.659 |
| 原逐通道逐 bin 标量循环（仅投影） | 80.060 | 117.803 |
| 当前批量 bin 原语（仅投影） | 10.354 | 11.322 |
| bin HTTP 热请求 | 41.115 | 49.147 |
| 范围 HTTP 热请求 | 18.767 | 22.767 |

这是同一轮合成输入对原标量循环与当前批处理的对照，不是旧/新完整 GUI 的性能对照。机器负载影响耗时，测试不设脆弱的绝对时间断言。

- 普通 RBP `result` 紧凑 JSON：**62,439 bytes**；将相同内部频谱带回时为 **97,016 bytes**，减少 **34,577 bytes（35.6%）**。比较只针对 `result`，不含外层身份/plan；五频段与质量证据仍保留。
- 自定义 `/bins` 实际响应 **211,389 bytes**，`/range` **7,983 bytes**。保留四组后端数值是为了单点游标不请求且不让前端计算科学值；这部分每新窗的 JSON/桌面解析开销仍存在，没有声称消除。
- 测量结束缓存驻留 3 项、约 **1.11 MB**（保守对象计量，非进程 RSS）。同一 NPZ 的频谱持久化空间、写入压缩以及每次内容 hash I/O 仍存在。客户端取消隔离旧响应，不代表已经启动的同步后端投影被中止。

### 本轮验证

- 新增/扩展测试覆盖：普通读取从未解压私有谱成员；公开与内部缓存并存且调用方不能修改缓存；范围拖动同窗不重复解码；项目范围拒绝发生在产物访问前；缓存字节/条目淘汰、删除释放、关闭后不驻留、并发合并；metadata/payload/digest 损坏及保留时间戳的损坏拒绝；非平坦谱批量结果逐项一致。
- 最终后端：**353 passed、22 deselected、0 failed**，用 `-k 'not iapf and not theta_beta'` 排除当前已有 5 s 旧夹具与 20/30 s 策略冲突的相关项。覆盖所有 `test_on_demand_*`、`test_dynamic_window_*`、`test_rbp_*`、自定义频率、标量/地形图、目录/official/combined、离线共享窗口和 TNK delivery；结果文件 `_temp/custom-frequency-performance-backend-final.xml`。
- 桌面受影响专项：**26 passed、0 failed**；覆盖自定义 API/VM、四项回放生命周期、XAML 控件、面板注册及 spectral 播放隔离，结果 `_temp/custom-frequency-perf-results/custom-frequency-perf-full.trx`。本轮没有重复执行整个桌面测试仓；初始阶段已确认的三项无关失败保留前述记录。
- OpenSpec strict 与 `git diff --check` 通过；43 个本次改动代码文件的规模检查无失败。全仓规模检查仅余前述两项既有失败。新增 0 依赖，未修改原始录音、默认 storage、操作系统配置或运行中的应用；未提交、推送、归档。OpenSpec 为 **17/18 完成**，仅 `4.2` 真实录音人工验收未勾选。

## 真实回放日志定位与配对生命周期修复（2026-11-06）

问题：首次打开自定义频段无数值；播放时更新稀少；波形明显卡顿。截图为暂停 50.352 s 和播放 56.039 s，ANT 默认 REF 导联、21 通道、1000 Hz、1.0X，算法 10 s 窗、1 s 步长。调查读取现有源码和日志；本轮修复没有修改科学公式、默认数据库、录音、预处理或地形图路线。

### 证据与结论边界

- 日志为 `storage/logs/backend-20261106.log`（14:04:44–14:06:57）及 `%LOCALAPPDATA%/BrainPlatform/logs/desktop-20261106.log`（相关 session `3b90c87bcc2e4c34b6936b3a7e02d967`，recording `8e0c630a60d14913ae9f5c60d7b23edf` / 后端 `wpf-montage-8e0c630a-60d1-4913-ae9f-5c60d7b23edf-1c3a9ef23771`）。14:08:40 查询时没有仍运行的桌面/后端进程。本轮没有启动 GUI 或实录播放。
- **首次空白的确定根因**：14:05:01 两源请求 sample 50352；PSD 14:05:02、RBP 14:05:03 完成，RBP 实际为 [40000, 50000)。后端按既定步长返回窗口，桌面 `IsTargetDisplayed` 却要求返回游标等于目标 50352，拒绝触发自定义投影，因此两边“已更新”但表格仍空。没有通过改变后端窗口政策修复。
- **更新稀少的确定根因**：播放每秒独立替换 PSD/RBP 等待者，旧窗口虽然在后端完成，但桌面只允许最新目标绑定。RBP 比 PSD 慢，两个当前窗口难以同时满足门槛。整段桌面 PSD 成功绑定 56 次、RBP 20 次，后端 `/bins` 仅执行 6 次（73.6–129.5 ms，中位数 108.0 ms）。例如 PSD106000 已在 14:06:55.342 绑定，RBP105000 在 14:06:55.382 才 ready；RBP106000 ready 时 PSD107000 已绑定。不能把后端“完成”直接等同于用户“看到”。
- **卡顿的已确认现象与限制**：14:06:54.772 的 tick P95 间隔 51.8 ms、P99 79.6 ms，而 tick 工作 P95 0.3 ms、最大 1.7 ms，说明存在 UI 调度/资源争用，不能归因于 tick 自身算法计算。21 条连续 `ChartBinding source=PublishedFrame` 来自 `UpdateSeries` 对 21 个通道逐个计时，并不证明完整波形重建 21 次。后端 41 个 RBP 帧的频谱读取中位数 328.9 ms、逐通道积分阶段中位数 191.7 ms、整帧中位数 570.1 ms；60 条缓存命中准入中位数 440.5 ms，这也不是单纯的 NPZ 解码耗时。修复消除未配对结果、取消/清空抖动和无界追赶压力，但现有日志不能量化修复后 GUI 帧率或把全部卡顿锁定到某一个底层资源。

### 修复边界与验证

- `RecordingReviewViewModel.SpectralWindows.cs` 统一拥有数值面板的两源读取和当前投影。先等同目标的 PSD/RBP 完成，再按实际范围、科学配置、源身份和通道顺序校验；只有一个活动事务和一个可替换的最新播放目标，tick 不等待也不反复取消活动事务。后端窗口与步长不变，处理不及时时跳过尚未开始的中间显示目标。
- 删除由每个 `PropertyChanged` 触发的配对投影/清空；两源完成后只触发一次。首次打开不再要求“返回游标=用户游标”。普通 PSD/RBP 图表和地形图仍使用原独立绑定路径。
- 等待下一窗及 bins 时保留上次发布的数值和曲线，窗口文本保留真实样本范围，状态明确标记等待目标和当前仍显示的范围；源或投影失败时清空。显式 seek、时间轴拖动、配置/导联变化和关闭立即取消、失效和清空，晚到响应不得恢复旧值。单点/通道切换仍不发请求。
- 新窗的自定义范围投影立即执行；180 ms 防抖只用于用户编辑范围。开启现有性能诊断时，新增 `review_spectral_pair_perf`，记录 session、目标样本、两个窗口 ID、source/projection 分段耗时和最新待处理目标，供复测定位剩余调度成本。
- 新增/扩展回归 8 项：两种数值面板的非整秒首次打开；慢 RBP 下保留数据并让两源跳到同一最新目标；慢 bins 和慢 range 下完成当前投影、不被连续 tick 饿死；首次源加载期间 seek 正常取消旧打开操作，不误报准备失败、不恢复旧窗；按下时间轴开始拖动即取消范围投影并清空；新 bins 未返回时修改范围仅保存选择，等网格发布后再投影，不把新源范围响应应用到保留的旧 bins。验证波形位置继续前进、行/曲线对象不在等待时清空、来源不混窗、请求数有界。
- 当前最终桌面专项 **36 passed、0 failed**，包含新生命周期、既有关闭/定位取消、通道与范围选择、HTTP、真实 WPF 控件实例及图表复用测试。结果 `_temp/spectral-window-repair-results/spectral-focused.trx`。此前扩展 Review + HTTP + DynamicWindowTargetCoordinator 回归 **408 passed、3 failed / 411**，失败名称与前述已复现基线相同：导出实录未配置、status 通知断言、播放中拖动状态断言；没有将其计为通过。结果 `_temp/spectral-window-repair-results/spectral-window-repair.trx`。
- 后端保存窗、自定义投影、RBP 按需相关回归 **31 passed、0 failed**（隔离 `BRAIN_STORAGE_ROOT`），包括封禁 raw/Welch 重算后的保存源投影、五频段与单位/质量契约。结果 `_temp/spectral-window-repair-backend.xml`。本轮未修改后端生产代码；没有用扩大缓存或改变科学窗口来掩盖等待问题。
- 当前全部 46 个改动/新增代码文件的规模评估通过；OpenSpec strict 和 `git diff --check` 通过。编译保留原有 NU1701/xUnit 警告；真实录音修复后 GUI 及长时间帧率未验收，OpenSpec 为 **20/21 完成**，4.2 仍开放，未提交/推送/归档。

## 剩余验收

- 初始 XAML、曲线/双边界控件、滚动排序与完整模板覆盖已完成；后续可继续由用户调整布局样式。
- 真实录音 GUI 验收：同窗对照 PSD/RBP、范围和通道切换、播放/seek 时延、长录音连续拖动、质量不可用状态。当前测试不能证明实际界面帧率或部署环境的响应时间。
- 后端与桌面需共同部署；本次仅提交源码并归档，未执行部署。按用户明确指令归档并保留任务 4.2 未完成，真实录音人工验收仍须另行完成。

## PSD 悬停频率对齐修复（2026-11-06）

- 图表悬停改用 SciChart 的 X 方向曲线吸附，关闭插值；曲线 tooltip 与横轴游标标签均以当前 PSD 返回的实际频率轴为来源。
- 根因通过当前 SciChart 9.1.0.29201 的实际渲染控件复现：启用 `CrosshairToSeriesX` 后，tooltip 已命中 3 Hz，`XAxisInfo.DataValue` 仍为鼠标原始坐标约 2.6，普通轴标签仍显示 2.6 Hz。原生曲线吸附本身不能保证底部标签同步吸附。
- 标准 `NumericAxis` 使用 `FrequencyCursorLabelProvider`；`AxisBase.FormatCursorText` 会委托该 LabelProvider，不需要自定义坐标轴。标签直接调用当前同一个 `XyDataSeries.FindIndex(..., SearchMode.Nearest)` 并读取该索引的 X 值，复用图表依赖的查找规则，不复制频率数组、不假设固定步长；中点按该规则处理，本次 2.5 Hz 命中 3 Hz。空序列/非有限坐标返回空文本，无可用 PSD 时停用 hover。普通轴刻度不变，该逻辑只负责显示，不改变选择或发起后端请求。
- 自定义频率专项独立输出构建后 **18 passed、0 failed**，其中 3 项 `ReviewCustomFrequencyPanelTests` 覆盖 modifier 悬停入口的标签、最近 bin、中点、非均匀频率间隔、切通道、范围选择不受悬停修改、空数据和 chart/series 复用。结果 `_temp/hover-snap-results/custom-frequency.trx`。
- 另以现有应用中的 SciChart 初始化配置运行隔离渲染探针，对实际 tooltip 和底部标签做断言：2.4→2、2.5→3、2.6→3、49.9→50 Hz，四点均命中相同频率和原始 PSD Y 值；带渲染的换导联、范围不变、无数据时停用 modifier 和空标签断言也通过。禁用 modifier 后原生交叉线不可见，但第三方仍保留 `SeriesInfo` 缓存，因此不要求缓存集合清空。常规测试宿主未初始化许可证，不能将其空 tooltip 当成命中验证；渲染探针单独补足该证据，没有复制或输出许可内容。
- 真实录音下物理鼠标悬停的最终视觉验收仍未完成；本轮未重测整体回放时延，未运行 OpenSpec strict（当前 CLI 不在 PATH），未修改算法或后端数据。

## 频率标签与底部手柄样式（2026-11-06）

- 按参考图将游标标签放到绘图区顶部，采用蓝底白字圆角样式；底部使用白边蓝色圆形手柄。两者均属于原生 `VerticalLineAnnotation`，沿用原拖动、显隐及实际 bin 吸附路径。顶部和右侧预留标签空间，窄范围只避让标签框，虚线和圆圈仍保持实际频率坐标。
- 自定义频率专项 **19 passed、0 failed**，结果 `_temp/custom-frequency-cursor-style-results/cursor-style.trx`。新增布局回归覆盖 1–3 Hz、1000/620 窗宽、50 Hz 边界、手柄配置、吸附及序列对象复用。普通测试宿主没有 SciChart 许可，因此标签尺寸由测试显式测量，不把该测试作为原生渲染验证。
- 隔离探针复用现有应用的 SciChart 初始化，真实渲染断言通过：顶部标签文字随吸附位置更新、1–3 Hz 标签不重叠、两种窗宽标签不越界、圆圈中心与游标 X 坐标对齐、50 Hz 标签可见、清空时隐藏游标。渲染图位于 `_temp/frequency-cursor-style-probe/labels.png`，仅使用合成数据；没有复制或输出许可内容。
- 未做真实录音物理鼠标拖动及播放性能复测；未运行 OpenSpec strict（当前 CLI 不在 PATH）。本轮仅调整呈现及对应验证，未新增科学计算或请求路线。

## 单次按下拖动与整段范围平移（2026-11-06）

- 图表统一拥有鼠标捕获和拖动状态，关闭原生注解的编辑拖动，避免先选中再编辑的双重交互。单频线、顶部标签、底部圆圈一次按住即可拖动；点击绘图区空白也能在同一次按下后连续拖动。绘图区外仅注解本身可触发，不因横坐标靠近游标而误启动。
- 范围阴影内部支持整段平移，保持原 Hz 宽度、两端吸附后端实际频点并在网格边界停止。端点仍可单独拖动。位移始终相对按下位置计算，避免累计吸附误差；非均匀网格只选保持原 Hz 宽度的合法端点对，不暗中改变范围宽度。
- `SetRange` 先写入完整端点对，再发布属性通知。现有 180 ms 防抖与取消/过期响应保护保持；集成测试验证整段平移只请求最终完整范围，不重新加载 PSD/RBP 科学窗口或 bin 数据。拖动选择不重建曲线或数据序列；科学计算、单位、通道顺序、API 与播放调度均未变更。
- 鼠标捕获丢失、模式切换、频率网格替换、清空、模型替换和控件卸载均结束拖动。交互代码按职责放在同一控件的 `ReviewCustomFrequencyChart.Interaction.cs`，没有新增通用交互框架。
- 最终桌面专项 **36 passed、0 failed、0 skipped**，结果 `_temp/custom-frequency-gesture-results/gestures-and-lifecycle.trx`。新增四项 WPF 手势回归和一项范围请求生命周期回归，覆盖单次按下、线/标签/圆圈/阴影、完整端点通知、触边、非均匀网格、捕获释放与对象复用。
- 隔离探针复用现有 SciChart 初始化配置，手势与标签布局断言通过，合成渲染预览仍为 `_temp/frequency-cursor-style-probe/labels.png`。专项测试通过实际 WPF 控件、坐标转换、鼠标捕获和交互入口调用验证；它们没有模拟真实物理鼠标的完整事件路由，不能替代用户拖动验收。
- 未做真实录音下物理鼠标拖动或播放性能复测，原任务 4.2 保持开放；OpenSpec strict 未运行（当前 CLI 不在 PATH）。没有提交、推送或归档。

## 频率输入悬停加减（2026-11-06）

- 单频点及范围两端输入框在悬停或键盘聚焦时显示上下箭头，固定预留 18 DIP 按钮槽，避免文字随显隐移动；点击移动到上一个/下一个实际后端 bin，长按使用 WPF `RepeatButton` 连续调整。键盘上下键走同一选择入口，采用 PreviewKeyDown 避免 TextBox 原生处理先吞掉方向键；Enter 和失焦输入提交保持。
- 加减先提交当前文字，格式错误或非有限数值不使用旧值替代；无 bin 时禁用，轴边界停止，范围端点不能通过加减越过另一端。仍通过原选择属性触发单点本地显示和范围防抖投影，没有新增科学计算、API 或播放调度。
- 规模评估：页面原 391 行，加入输入模板后会越过目标线，按职责提取调色板与表格呈现样式至 `ReviewCustomFrequencyStyles.xaml`；输入样式及事件留在面板。新增输入测试作为原面板测试类的 partial，共用既有控件宿主和契约夹具，没有复制测试基础设施。
- 桌面专项 **37 passed、0 failed、0 skipped**，结果 `_temp/custom-frequency-input-results/input-steppers.trx`。新增控件测试验证模板悬停配置、真实点击和键盘路由、非均匀轴 3→50 Hz、上下限、范围不交叉、错误输入、无数据禁用与曲线对象复用。保留此前拖动和范围投影生命周期回归。
- 使用现有 SciChart 初始化配置的隔离渲染探针通过相同交互测试，并确认聚焦输入的箭头实际显示为 18×24 DIP；预览 `_temp/frequency-cursor-style-probe/input-steppers.png` 已人工查看，文字与上下箭头未重叠。本探针使用合成数据，没有移动系统鼠标；真实物理悬停、长按手感及实录播放仍待用户验收。
- 未运行 OpenSpec strict（当前 CLI 不在 PATH）；未提交、推送或归档，4.2 真实录音验收保持开放。

## 归档发布检查（2026-11-06）

- 按用户“提交推送代码吧！归档”的明确指令，将当前源码、测试、文档及 OpenSpec 纳入本次提交；生成物、临时探针、日志、数据库及录音不纳入提交，不执行部署。
- 主规范同步已逐项核对：`desktop/review-spectral-values` 新增 3 项 requirement；`analysis/official-rbp-faa-brainbeat-execution` 修改 1 项并新增 1 项；未涉及的 requirement 和原场景完整保留。变更 strict 验证通过；88 份主规范验证全部通过。
- 发布前相关后端回归 **124 passed、0 failed、0 skipped**，桌面回归 **64 passed、0 failed、0 skipped**。结果为 `_temp/publish-custom-frequency-backend.xml` 和 `_temp/publish-custom-frequency-results/publish-custom-frequency.trx`；覆盖自定义投影、窗口基础设施、标量/地形图，以及桌面交互、生命周期、诊断与窗口边框。它们是本轮专项回归，不替代此前记录的全仓基线和真实录音验收。
- 75 个改动/新增代码文件规模评估通过，超限文件已有职责与拆分评估；tracked diff 空白检查通过，提交前继续检查完整暂存区。
- 回放窗口使用系统非透明窗口边框与 Windows 11 圆角偏好；窗口句柄、圆角属性和正常/最大化状态回归通过。未对真实桌面阴影视觉效果进行目视验收。
- 归档路径：`openspec/changes/archive/2026-11-06-add-review-custom-frequency-inspection/`。任务 **4.2 真实录音人工验收仍未完成**，保持未勾选；本次归档接受该验证缺口，不宣称实录数值对照、物理鼠标手感、连续播放帧率或长时间性能已通过。旧计划 `add-psd-all-band-topomap` 保持 active。
