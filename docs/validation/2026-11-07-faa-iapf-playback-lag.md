# FAA / IAPF 播放滞后排查及修复证据

日期：2026-11-07。基线：`8fc7ff8` 上的未提交工作区；承接 `fix-cold-spectral-playback-lag`，保留此前 PSD/RBP 数值面板和样式修改。本记录覆盖 FAA/IAPF 后续修复，不替代前一阶段的实验记录。

## 实验与根因

用户报告 FAA 在记录 86–104 s、IAPF 在 104–134 s 滞后。核对的会话为 `a5f34b5e14ec4b50840f58b3eaf7b7ab`，1000 Hz、1X；桌面 recording ID 为 `8e0c630a60d14913ae9f5c60d7b23edf`，后端为 `wpf-montage-8e0c630a-60d1-4913-ae9f-5c60d7b23edf-1c3a9ef23771`。

- 桌面日志：`C:/Users/23998/AppData/Local/BrainPlatform/logs/desktop-20261107.log`，相关实验约 01:18:49–01:20:25。
- 后端日志：`D:/AI/Work/brain-platform/storage/logs/backend-20261107.log`。
- 上述记录范围内 `review_analysis_result_bound`：FAA 20 条，请求到绑定中位 448 ms、最大 596 ms；IAPF 33 条，中位 210 ms、最大 277 ms。包含已命中结果的请求，不能全部解释成冷计算成本。
- FAA 后端 01:18:49–01:19:11 的 19 条 `dynamic_window_source_prepared`，worker 启动至源准备完成中位 254 ms；相应科学计算中位仅 3.1 ms。后端时间戳只有秒精度，不能据此精确拆分跨进程墙钟时间。
- 当时 `pending_count=0`，计算槽等待接近零，不支持无限排队积压的判断。134.561 s 处约 1478.7 ms 的 Dispatcher 样本发生在暂停之后，不能当作持续播放期间主线程卡死的证据。

源码确认两处主要原因及一处附加成本：

1. `_RecordingAlgorithmContext.load_faa_signals` 原先用上下文私有 `_signals` 保存整份连续滤波结果。每个动态窗口新建并关闭上下文，所以相邻窗口仍重新读取、滤波全记录；并未跨窗复用。日志可见完整的 `0:65536`、`65536:131072`、`131072:149982` 读取，与目标 20 s 窗口不同。
2. 原先提前准备仅接入 PSD/RBP 数值面板。FAA/IAPF 仍在到达目标步进后请求，即使单窗耗时小于 1 s，也固定晚于波形游标显示。
3. `DynamicWindowObserver` 在返回终态窗口前等待取消订阅接口，样本约增加 10–15 ms；这不是主要计算耗时，但属于不必要的交付等待。

## 修复及科学边界

- 将既有连续预处理实现抽至 `ContinuousPreprocessingService`，由 `RecordingService` 创建并同时供频谱服务与 FAA 委派使用；复用原有有界 LRU / single-flight，不新增永久缓存或兼容分支。FAA 只准备配置中的有序双通道，活动上下文保留映射所有者引用，避免 LRU 淘汰时提前关闭仍在使用的数据。
- 保持整份记录的连续零相位滤波，再按实际绝对样本范围切片；不改 FAA/IAPF 公式、质量门、参考、V/float64 或通道顺序。源内容、采样率、导联/参考、单位、滤波、通道顺序、实现版本变化均不可误命中旧源。
- 将既有两目标队列泛化为 `ReviewPlaybackBuffer<T>`。FAA/IAPF 经原后端适配器串行提前准备至多两个未来步进窗，准备不发布；游标到点才走原身份校验和绑定。窗口/步长继续读取后端配置；本实验 FAA/IAPF 为 20 s / 1 s，不套用 PSD/RBP 的 10 s 窗长。TNK 不接入这条无状态准备路径。
- 过载时保留一个活动目标和一个最新待处理目标，丢弃过期排队目标；不反复取消活动科学计算造成饥饿。暂停、seek、拖动、配置变化及关闭会取消准备，晚到响应不能回刷。拖动结束先请求选定窗口，再恢复该窗口之后的准备。
- 终态结果的订阅释放独立执行，仍使用原有 3 s 清理令牌并观察异常；尚未完成的取消请求仍等待自身订阅清理。原有 100 ms 发现及后退轮询间隔不变，不以加密轮询换时延。
- 增加 `continuous_source_perf`、`review_scalar_prepare_perf`、`review_scalar_playback_perf`；准备成本与到点等待分开，记录真实窗口结束时间。`delivery_detached` 标识终态释放是否脱离交付。播放位置来自最近一次游标通知，不是同步硬件时钟。

HTTP/WebSocket 消息、数据库结构、结果产物格式和原始记录不变；后端资源预算不放宽。需同步运行更新后的后端和桌面端。

## 自动验证

后端在 `backend` 目录执行：

```powershell
.venv/Scripts/python.exe -m pytest tests/test_faa_shared_preparation.py tests/test_faa_continuous.py tests/test_faa_on_demand_capability.py tests/test_iapf_algorithm_module.py tests/test_official_algorithm_runs.py tests/test_spectrum_service.py tests/test_prepared_source_identity.py tests/test_prepared_source_cache.py tests/test_block_preprocessing.py tests/test_stft_bounded_preprocessing.py tests/test_rbp_channel_quality.py tests/test_on_demand_resource_budgets.py tests/test_on_demand_dynamic_service.py tests/test_custom_frequency_window_api.py tests/test_on_demand_rbp_api.py tests/test_on_demand_psd_api.py -q
```

结果：192 通过，54.51 s，2 条既有弃用警告。新增 FAA 源测试 10 项：42 s、250 Hz、三通道合成记录的独立上下文在 0、1、20 s 取窗，只发生一次全记录双通道预处理；与独立 SciPy 连续滤波切片比较 `rtol=1e-11, atol=1e-18 V`。另覆盖七类身份变更、并发冷准备合并、LRU 淘汰后的活动映射生命周期。既有 FAA 全量/按需、IAPF 数值、PSD/RBP 投影与资源测试通过。

桌面在仓库根目录执行，构建输出隔离，不停止运行中的应用：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --filter '(FullyQualifiedName~Tests.Review&FullyQualifiedName!~Provided_recording)|FullyQualifiedName~DynamicWindowObserverTests|FullyQualifiedName~DynamicWindowDeliveryTests|FullyQualifiedName~OnDemand' -v normal --nologo -p:UseSharedCompilation=false -p:OutputPath=D:/AI/Work/brain-platform/_temp/scalar-lag-build/ --logger 'trx;LogFileName=scalar-review-final.trx' --results-directory D:/AI/Work/brain-platform/_temp/scalar-lag-results
```

结果：513 项，511 通过、2 失败；源码构建 0 错误。本轮新增 16 项通过：FAA/IAPF 连续步进、两未来窗不提前发布及不重复请求、暂停/seek/拖动/配置/关闭隔离、过载最新目标、失败呈现、TNK 排除；IAPF 页面级播放/暂停/拖动/关闭接线；终态释放和取消的三个协议用例。此为受控任务/时钟测试，不代表实录帧率。

两项失败与前一阶段相同，已在修改前源码 overlay 复现，证据为 `_temp/cold-spectral-baseline-tests.log`、`_temp/cold-spectral-baseline-sources/baseline.targets`：

- `Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`：期望 1、实际 0。
- `Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`：期望暂停、实际仍播放。

未借本次修复修改上述无关断言或隐藏失败。NU1701、CA2022、xUnit2013 等既有构建警告仍在。`Provided_recording` 环境依赖用例未执行。

本地输出：`_temp/scalar-lag-backend-final-tests.log`、`_temp/scalar-review-final-tests.log`、`_temp/scalar-lag-results/scalar-review-final.trx`。临时构建及测试产物不提交。

严格 OpenSpec、`git diff --check` 与变更 C#/Python/XAML 文件规模检查通过。连续源 112 行；绑定按播放准备生命周期拆为 partial。超过 400 行的记录服务（537）、频谱服务（424）和信号输入适配器（410）在 `docs/code-size-policy.json` 复核登记，不机械拆成编号文件。

## 尚待实录验收

本次未启动或操作 GUI、未删除真实缓存、未测修复后真实录音响应体积及长时间吞吐量。不能宣称已经实测零滞后。初次冷打开仍须读取、滤波；提前准备只能移动计算时间，持续生产速度不够时仍会显示真实等待状态。切换算法/参数可能淘汰有界源，两个未来窗也可能产生用户未访问的既有窗口产物。

在同一 PowerShell 中设置后启动新版后端与桌面：

```powershell
$env:BRAIN_REVIEW_PERF_DIAGNOSTICS = '1'
$env:BRAIN_REVIEW_PERF_SCENARIO = 'faa-iapf-after-fix'
```

先以 1X 在未计算区段分别连续播放 FAA/IAPF 60–120 s，并测试暂停恢复、seek、拖动和关闭；再单独测试 2X。日志位置保持不变。对照实际 window ID，确认首次源准备后相同身份的 `continuous_source_perf lookup_hit=True`、未来窗 `publication=false`、到点 `buffer_state=ready/waiting/miss`、实际窗口滞后与晚到隔离，不能拿缓存命中的发布成本冒充冷科学计算成本。测试代码也会写日志，需依据会话、记录和场景筛选实录。

## 后续用户实录日志核对

用户完成 FAA 29–50 s、IAPF 50–70 s 实验后，核对同日 12:29:49–12:32:10 的 `review_session_id=a6b49b1ea644474384af4f055273bff9`。记录身份与上文一致，1000 Hz、1X，场景标签为 `unspecified`，未混入自动化测试记录。

| 观测项 | FAA | IAPF |
| --- | ---: | ---: |
| 稳定播放发布目标 | 30–50 s，共 21 次 | 51–70 s，共 20 次 |
| `buffer_state=ready` | 21/21 | 20/20 |
| 到点等待中位 / 最大 | 1.809 / 3.709 ms | 1.579 / 1.855 ms |
| 发布时窗口差中位 / 最大 | 14 / 29 ms | 15 / 46 ms |
| 提前准备耗时中位 | 192.082 ms | 177.815 ms |
| 首次当前窗请求到绑定 | 325 ms（29.311 s） | 526 ms（50.171 s） |

后端 FAA 24 个、IAPF 23 个窗口的结果均为 `cache_hit=False`，包含提前准备且可能未显示的未来目标；不是旧窗口产物命中实验。两种算法的连续预处理源各首次 `lookup_hit=False`，随后分别命中 23、22 次。窗口结果仍逐窗新算，连续源复用避免重复整份滤波；到点等待不代表科学计算耗时。

相关桌面队列记录均为 `pending_count=0`，无警告或错误。播放中的四个资源样本 Dispatcher 等待为 0.1、6.3、4.0、1.2 ms；初始打开及暂停期间仍有较大样本，不能据此保证整个界面零卡顿。

这次短时 1X 连续播放未重现原先持续滞后，提前准备和跨窗源复用均已在实录日志中确认。时间差使用最近播放游标通知，不等于屏幕实际绘制延迟；长时间/2X、范围交互及完整 GUI 验收仍未覆盖。未操作原始数据或真实缓存；归档状态以 OpenSpec 目录为准，提交状态以 Git 为准。

提交前与设置页缓存管理合并工作区复核：后端 FAA/IAPF、缓存维护、失败注入、离线复用、进程恢复和 TNK 交付相关测试 63 通过；桌面播放准备、交付、缓存管理、记录访问、滚动条及数值面板相关测试 58 通过，0 失败/跳过，构建成功。此次未重复全量回归，前述两项已知桌面失败结论保持不变。所有 5 个活动 OpenSpec 变更严格校验通过；变更文件规模及待提交路径检查通过。输出为 `_temp/publish-backend-tests.log`、`_temp/publish-desktop-tests.log`、`_temp/publish-results/publish.trx`。
