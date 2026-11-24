# 回放频段数值更新的职责拆分验证

日期：2026-11-06。OpenSpec：`add-review-spectral-values`。本记录覆盖首次实现之后的性能修复；首次实现和失败基线见[原验证记录](2026-11-06-review-spectral-values-report.md)。

## 根因和修复边界

首次实现把数值面板接到了完整地形图接口：每个匹配窗口请求五个 PSD 和五个 RMS 字段，空间点足够时，会生成最多 10 个 65×65 网格，表格只使用其中的逐导联值。前端还会清空行集合并重新添加所有导联行。这两项都是不必要的工作。

修复将“已有结果转标量”和“标量转空间网格”分成明确的所有者，直接迁移调用方，没有增加地形图回退、兼容别名、科学算法、科学参数开关或依赖：

- `backend/app/services/result_scalar_inputs.py`：唯一的 PSD/RBP 结果标量适配器。保留原 PSD 频段平均密度公式和原 RBP 数组读取，RMS 仍调用已有 `band_rms_uv` helper。原 map 专用适配器名字及导入直接迁移，无旧名别名。
- `backend/app/services/result_scalars.py`：只读取已保存的动态窗口，返回顺序固定的逐导联数值、单位、质量和 provenance。它不引用 map service，不读原始信号，不调用算法执行器、空间来源解析或插值。
- `backend/app/services/topomap.py`：使用同一套标量适配器，再负责空间点筛选、坐标和网格。保留现有地形图 HTTP 入口和字段含义，避免出现两套科学投影公式。
- `ReviewSpectralValuesViewModel` 和 `ReviewSpectralValueRow`：按有序通道建立稳定行，只有通道集合或顺序变化才重建；窗口、频段和不可用状态都更新原对象，`SetProperty` 只通知变化的属性。XAML 布局和四列单位没有因本次修复而改变。
- 波形 `SeekAsync`/`TickPlayback` 沿已有独立路径运行，不等待标量请求。延迟、取消和旧响应继续受现有源窗口引用、revision、配置身份与取消令牌约束。

分析窗口仍使用现有 PSD/RBP 动态策略，默认 10 秒窗口、4 秒 Welch 分段、50% 重叠。源数据、参考、滤波、通道顺序、绝对秒/样本范围、质量策略和官方结果数组不变。五频段一次读取，频段下拉只更新展示；PSD 不从 RBP 反推。

## API 和消费者

新增 `GET /api/analysis-windows/{window_id}/scalar-values`，显式 `field_ids` 最多 5 项，可带 `project_id`。返回 `ResultScalarsResponse`，包含窗口/记录/算法 ID、版本、实际范围及字段，不包含坐标、网格或图表对象。每个 channel point 仅包含 channel、value、quality_status、quality_reasons。

- 缺失窗口：404；非法字段：422；算法不支持或已保存结果合同无效：409。
- 窗口尚未可用：unavailable，附原因；无效数值、未知或 unavailable 的通道质量：null，附原因。缺数组、单位或维度错误保留 `ABSOLUTE_POWER_NOT_AVAILABLE`。
- 本次新增消费者是 Review 的 `IReviewScalarProvider`，由原 PSD/RBP adapter 提供；桌面 `HttpAlgorithmClient` 直接访问 scalar-values。HTTP 反序列化不捕获调用方 UI 同步上下文。
- 中央接入只增加 `main.py` 的标量 service/router 注册及客户端契约。数据库和记录文件无迁移。桌面和后端需要一起更新；数值面板不回退到旧地图请求。

## 自动化结果

| 范围 | 结果 | 证据 |
| --- | --- | --- |
| 后端相关回归 | 94/94 通过 | 原 PSD/RBP API、RMS、原地形图、标量 HTTP、同窗集成及规模策略测试 |
| 桌面专项 | 35/35 通过 | 标量 HTTP 路径、dispatcher、身份/单位、稳定行与容器、取消、延迟、seek/tick、montage |
| 全部 Review | 359/362 通过，3 失败 | 与首次实现基线相同的 3 个场景，详见下文 |
| 桌面构建 | 0 errors | 测试命令使用独立输出目录，未关闭运行中桌面进程 |
| OpenSpec 严格校验、差异空白 | 通过 | `openspec validate add-review-spectral-values --strict`、`git diff --check` |
| 本次目标文件规模评估 | 通过 | 新增生产文件小于 400 行；既有超限文件登记评估 |
| 全局后端规模检查 | 失败，既有 3 项 | 维持原基线问题，无本次新超限生产文件 |

后端新增测试在 `test_result_scalars.py`，将空间来源解析和插值替换成必定失败的哨兵，证明数值端点不会进入这些路径；同时与共享原地形图投影逐项核对数值、单位和 provenance。真实后端合成 WPF 记录通过现有 PSD/RBP 动态 API，再读取标量，核验两条通路的窗口/滤波/分段一致、保存结果不变、原文件 hash 不变。

桌面测试证明同一组 `ReviewSpectralValueRow` 在更新、切频段、清空显示和重复同通道初始化后保持引用不变、无 collection reset；实际 WPF `DataGridRow` 容器被复用。重复相同数值不发布属性通知。人为挂起标量返回时，波形 seek 完成且时钟继续，旧响应不覆盖新窗口。

本机证据在 `_temp/review-spectral-results/spectral-scalar-focused.trx` 和 `_temp/review-spectral-results/review-spectral-values-after-scalar-fix.trx`。离屏合成 WPF 图为 `_temp/review-spectral-values-tests/_temp/review-spectral-panel.png`，已目视检查 Theta 下拉和 F3/F7 四列数值。以上临时文件不进入源码交付。

后端命令（工作目录 `backend`）：

```powershell
.venv/Scripts/python.exe -m pytest tests/test_result_scalars.py tests/test_rbp_amplitude_projection.py tests/test_psd_topomap_density.py tests/test_topomap.py tests/test_review_spectral_sources.py tests/test_file_size_policy.py tests/test_rbp_band_rms.py tests/test_rbp_on_demand_capability.py tests/test_psd_on_demand_capability.py tests/test_on_demand_rbp_api.py tests/test_on_demand_psd_api.py -q --tb=short
```

桌面专项命令（工作目录仓库根）：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore -o _temp/review-spectral-values-tests --filter 'FullyQualifiedName~ReviewSpectral|FullyQualifiedName~Spectral_panel|FullyQualifiedName~Spectral_scalar|FullyQualifiedName~Late_projection|FullyQualifiedName~Closing_while_both|FullyQualifiedName~ReviewAlgorithmRegistrationTests|FullyQualifiedName~HttpResultScalarsTests|FullyQualifiedName~RbpReviewAnalysisAdapterTests|FullyQualifiedName~HttpAlgorithmClientDispatcherTests' --logger 'trx;LogFileName=spectral-scalar-focused.trx' --results-directory _temp/review-spectral-results --verbosity quiet
```

完整 Review 使用相同命令，把 filter 改为 `FullyQualifiedName~BrainPlatform.Desktop.Tests.Review`，TRX 文件名改为 `review-spectral-values-after-scalar-fix.trx`。

## 投影成本测量

21 个合成导联，197 个 PSD 频率点，每个源 5 个频段；同进程交替 map/scalar 顺序各执行 30 次。测量范围是从内存中已有窗口投影及 JSON 序列化，不含原始读取、科学执行、磁盘、HTTP 或播放帧调度。两条路径逐通道值相等。

| 路径 | 中位耗时 | 最大耗时 | 响应字节 | 网格单元 |
| --- | ---: | ---: | ---: | ---: |
| 原地图响应（共 10 字段） | 62.636 ms | 107.192 ms | 639,094 B | 42,250 |
| 标量响应（相同 10 字段） | 5.755 ms | 9.198 ms | 20,872 B | 0 |

脚本为 `_temp/review-spectral-scalar-benchmark.py`，输出 `_temp/review-spectral-scalar-benchmark.json`。这证明本轮移除了冗余空间计算及大响应，并非用户真实记录的端到端性能验收。

## 既有失败与未验证项

完整 Review 三失败仍是：`Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`、`Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`、`Provided_recording_exports_through_real_http_client_when_acceptance_is_configured`。最后一项缺真实 HTTP 验收配置，runner 把 dynamic skip 记为失败。这三项先前已在未修改 HEAD `aa4a6d6dbce32b2cd6f7853a411e00d17a5a133f` 复现，本轮也未新增失败场景。

全局规模仍报告 `algorithm_recording_context.py` 410 行未登记、`on_demand_dynamic.py` 558 行超过登记 530、`algorithms/psd/runner.py` 464 行超过登记 420；同样是原基线问题。此次目标文件检查通过，并更新了原 `test_topomap.py` 的规模登记（541 行，新增数值测试已分开）。NU1701/xUnit analyzer 和 FastAPI/TestClient deprecation warnings 保留。

尚未在运行中桌面使用用户真实记录，对照波形与数值面板进行播放、seek、长时间或压力验收。数值面板仍同时驱动现有 PSD/RBP 动态 binding，科学窗口本身的计算量存在；本次修复消除的是额外空间插值、网格传输和表格结构重建，不能据此保证所有来源的卡顿均已消失。未提交、推送、归档或重启用户程序。

## 同日追加：逐行悬浮提示可读性

用户截图显示 `ChannelQuality` 将整个 RBP quality 对象直接转为文本，包含嵌套 Welch evidence；行 `ToolTip` 直接绑定长字符串，导致窗口横向撑满。此次仅调整 `ReviewSpectralValuesViewModel` 的展示文本和 `ReviewSpectralValuesPanel.xaml` 的行提示，继续保留现有表格样式和稳定行。后端、请求、科学计算及地形图代码没有在这次展示调整中修改。

- 复用项目 `AppDescriptionToolTipStyle`，最大宽度 360 WPF 逻辑像素，自动换行。通过 `PlacementTarget.DataContext.Details` 读取当前悬停导联，避免共享提示绑定到旧行。
- 显示通道、频段、中文质量状态及原状态码、后端提供的有效/总分段与峰值、原因。隐藏嵌套 evidence JSON，失败保留 code/message；不可用状态仍依照原规则返回 null。
- 四项提示数值使用表格单位和六位有效数字，替代原始 ratio、V²/Hz 和 G17 浮点展开。只改变文本格式，底层行数值不舍入、不改变。表格自身仍沿用已有两位小数。
- 桌面同一专项 filter 最终 **41/41 通过**，构建 0 errors。新增覆盖正常/警告/未知/不可用质量摘要、嵌套 JSON 隐藏、失败原因和底层精度，以及真实 WPF 提示限宽、长原因换行、切换导联/频段/清空后的绑定。

最终 TRX 为 `_temp/review-spectral-results/spectral-tooltip-focused.trx`。提示离屏图为 `_temp/review-spectral-values-tests/_temp/review-spectral-tooltip.png`，已目视检查。OpenSpec strict 和差异空白校验通过；本次四个目标代码文件均低于 400 行。没有重跑后端测试或完整 Review 集合，前文结果属于上一轮验证；尚未在用户运行中的回放页验收。

## 同日追加：移除 RBP 叠加图的质量汇总悬浮提示

按用户截图要求，只删除 `RbpReviewAnalysisPanel.xaml` 导联容器上的 `ToolTip="{Binding QualitySummary}"`。保留图表频段数值提示、质量模型、算法详情页质量说明和地形图功能。生产代码改动为一行，文件低于 400 行。

桌面编译通过；相关 `RbpReviewChartTests`、`RbpReviewPresentationTests`、`RbpResultPreviewTests`、`RbpOnDemandDetailTests` 合计 36/38 通过。失败为 `SciChartTrend_LeavesRejectedWindowAsNanGap`、`CurrentVersion_SciChartTrendContainsFiveSeriesIncludingGamma`，均预期 5 条曲线实际 0。它们直接实例化未 Loaded 的 `RbpChart`，未引用此次修改的回放面板；现有图表在 Loaded 时建立曲线。没有修改这两个测试或图表生命周期，也没有额外执行隔离 HEAD 基线。TRX：`_temp/review-spectral-results/rbp-remove-quality-tooltip.trx`。OpenSpec strict 和 diff check 通过；未在运行中的桌面验收。

## 归档交付记录

2026-11-06，用户反馈已完成测试，并明确授权提交、归档及上传 GitHub。该反馈未附具体测试步骤或性能测量，因此不扩展为全量或压力验收结论。保留以上自动化结果与失败记录；归档交付阶段不重复执行已记录的测试。

正式规格同步至 `openspec/specs/desktop/review-spectral-values/spec.md`，变更归档目标为 `openspec/changes/archive/2026-11-06-add-review-spectral-values/`。交付包含前后端源码、测试、规格和验证文档；临时测量、构建输出、TRX 和离屏截图不随源码提交。
