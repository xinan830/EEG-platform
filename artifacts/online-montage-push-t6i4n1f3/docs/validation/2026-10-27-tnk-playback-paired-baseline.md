# TNK 回顾播放配对基线

状态：待真实桌面测量。本文是 4.1 的操作记录模板；代码测试、Release 编译和后端微基准不能替代这项测量。

## 固定条件

- 选择一份通过 TNK 五角色输入校验的本地录制，并确认 TNK 所需角色、采样率和滤波设置有效。既有验证记录指出 `6.16高发展` 缺少 Oz，未经确认的替代映射不能用于这次比较。
- 两个场景使用相同的 recording ID、角色映射、TNK 参数、波形显示滤波/参考、图表面板和视窗大小。
- 固定 1x 播放，从相同游标位置连续播放 60 秒；每次开始前都暂停并回到该位置。先打开工作台并等待加载稳定，再开始计时段。
- 保持 TNK 工作台打开。`waveform_only` 关闭按需分析；`waveform_tnk` 开启 TNK 按需分析和播放。其他设置不变。
- 做 3 组配对，每组各跑两个场景；交替顺序（第 1、3 组先 waveform-only，第 2 组先 waveform+TNK），减少启动/缓存顺序对比值的影响。测试期间不要做其他高负载操作。

## 启动方式

在启动桌面程序的 PowerShell 窗口设置诊断变量。每次切换场景都先退出桌面程序，再设置新场景并重新启动；在已运行的进程里修改环境变量不会生效。

```powershell
$env:BRAIN_REVIEW_PERF_DIAGNOSTICS = '1'
$env:BRAIN_REVIEW_PERF_SCENARIO = 'waveform_only'
dotnet run --project D:\AI\Work\brain-platform\desktop-client\BrainPlatform.Desktop\BrainPlatform.Desktop.csproj --configuration Release
```

另一场景仅将值改为：

```powershell
$env:BRAIN_REVIEW_PERF_SCENARIO = 'waveform_tnk'
```

启动后在同一录制和 TNK 工作台中设置对应的按需分析开关。桌面诊断写入 `%LOCALAPPDATA%\BrainPlatform\logs\desktop-YYYYMMDD.log`；后端阶段日志位于项目配置的 `storage\logs\backend-YYYYMMDD.log`。

## 记录与判读

每组记录两个场景的日志区间，并填写 recording ID、角色映射、起止游标、播放速度、TNK 配置、桌面版本和后台是否空闲。统计时不要混合不同 recording ID 或配置。

| 场景 | 组别 | tick 间隔 p50/p95/p99 ms | tick 处理 p50/p95/p99 ms | TNK 结果 lag s | polling 请求/5s、请求耗时 p95 ms | 图表追加/重建耗时 p95 ms | 覆盖状态/缺口 | 队列峰值 |
|---|---:|---|---|---:|---|---|---|---:|
| waveform_only | 1 | 待测 | 待测 | — | — | 待测 | — | — |
| waveform_tnk | 1 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 |
| waveform_only | 2 | 待测 | 待测 | — | — | 待测 | — | — |
| waveform_tnk | 2 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 |
| waveform_only | 3 | 待测 | 待测 | — | — | 待测 | — | — |
| waveform_tnk | 3 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 | 待测 |

日志事件：`tnk_playback_tick_perf` 是 33 ms 播放计时器的间隔和同步处理时间；`tnk_playback_slow_tick` 单独记录超过 80 ms 的 tick 及其游标更新、按需目标提交耗时；`tnk_playback_window_target` 与 `tnk_playback_window_observed` 用 request_id 关联目标进入协调器及窗口响应总耗时；`tnk_playback_ui_dispatch` 记录结果从线程池排入 WPF Dispatcher 的等待；`tnk_playback_history_projection` 和 `tnk_playback_history_applied` 记录后台历史投影、Dispatcher 排队和界面应用耗时；`tnk_playback_poll_perf` 是 5 秒内事件轮询请求数、返回帧数和 HTTP 客户端往返耗时；`tnk_playback_event_delivery` 记录结果窗口结束样本相对最近一次目标游标的滞后及发布后投递年龄；`tnk_chart_update_perf` 区分追加与重建，记录窗口结束时间及图表更新耗时。后台的 `scientific_compute_acquired`、`dynamic_window_source_prepared`、`tnk_on_demand_execution`、`dynamic_window_computed` 和 `dynamic_window_ready` 用来分辨槽等待、输入准备、状态重放/科学计算及结果持久化耗时。`review_analysis_queue` 与播放事件缺口/coverage 用来检查积压和历史完整性。

从桌面日志提取本次指标：

```powershell
$log = Get-ChildItem "$env:LOCALAPPDATA\BrainPlatform\logs\desktop-*.log" |
  Where-Object { $_.Name -notmatch '\.\d+$' } |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
Select-String -Path $log.FullName -Pattern 'tnk_playback_tick_perf|tnk_playback_poll_perf|tnk_playback_event_delivery|tnk_chart_update_perf|review_analysis_queue|tnk_playback_delivery_gap'
```

**4.1 完成条件：**三组配对数据和相应日志齐全，场景的录制/映射/参数一致，播放期间没有未解释的 coverage 缺口。完成测量后再基于实测结果定义 4.2 门槛；本文件不预设性能阈值，也不宣称桌面已通过验收。
