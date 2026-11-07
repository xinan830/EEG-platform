# 冷缓存 PSD/RBP 数值面板播放滞后修复记录

后续 FAA/IAPF 的共享连续源及播放接线修复另见 [FAA/IAPF 验证记录](2026-11-07-faa-iapf-playback-lag.md)。下文统计和“无后端修改”等范围说明只对应此前 PSD/RBP 阶段；其中准备队列后续已泛化为 `ReviewPlaybackBuffer<T>`。

日期：2026-11-07。OpenSpec：`fix-cold-spectral-playback-lag`。基线提交：`8fc7ff8`，本次为其上的未提交工作区修改。环境：Windows / PowerShell 7，WPF / .NET 10，项目既有 Python 虚拟环境。

## 问题与日志证据

用户清除缓存后重新做播放实验，数值面板再次滞后。检查的实验为本地时间 2026-11-07 00:19–00:21：1X、1000 Hz、21 导联、197 个频点。分析继续沿用既有 10 s 窗长 / 1 s 步长；滤波、参考、Welch 分段及导联顺序取当前分析快照，本次未改参数。

- 桌面日志：`C:/Users/23998/AppData/Local/BrainPlatform/logs/desktop-20261107.log`。
- 后端日志：`D:/AI/Work/brain-platform/storage/logs/backend-20261107.log`。
- `review_session_id=f3bd842ed7284a55b78f882b7706472d`。
- 桌面 recording ID：`8e0c630a60d14913ae9f5c60d7b23edf`。
- 后端 recording ID：`wpf-montage-8e0c630a-60d1-4913-ae9f-5c60d7b23edf-1c3a9ef23771`。

按该会话全部 88 条 `review_spectral_pair_perf` 及对应表格应用记录统计：

| 观测项 | 中位数 | 最大值 |
| --- | ---: | ---: |
| PSD/RBP 配对源等待 `source_ms` | 662.55 ms | 1363.9 ms |
| 配对后的数值投影 `projection_ms` | 95.35 ms | 156.2 ms |
| 配对完成时实际窗口滞后 `window_lag_s` | 0.7575 s | 1.671 s |
| 表格应用时实际窗口滞后 | 0.734 s | 1.641 s |
| 表格应用 `apply_ms` | 2.1815 ms | 22.674 ms |

配对日志和表格应用日志的采样时刻不同，不能混为同一项时延。`source_ms` 包含窗口请求、后端排队/计算、返回及绑定等等待，不能解释成纯 Welch 耗时；`apply_ms` 也不是完整 GUI 帧耗时。现有证据支持主要等待发生在窗口源和投影阶段，不支持“两位小数格式化导致主要滞后”，也不支持据此声称存在无限请求积压。

旧调度在播放到达目标时间后才开始读取当前 PSD/RBP 和投影，下一目标还要等待当前投影结束。即使单窗平均成本小于 1 s，也会持续表现为到点才计算的滞后。缓存命中只能掩盖这部分等待。

## 实现与边界

1. `ReviewAnalysisCoordinator` / `ReviewAnalysisBinding` 分开准备和采用窗口。准备沿用原适配器请求及科学身份校验，不改变 `CurrentWindow`；采用仍核验录制、实际样本范围、参数身份、单位、通道顺序和质量。
2. `ReviewSpectralPlaybackBuffer` 串行准备最多两个未来步进目标，先取现有 PSD/RBP 窗口，再走既有后端 bins/range 或五频段标量投影。准备完成不发布；播放到对应目标时才采用。
3. ViewModel 保持一个活动前台目标和一个最新待处理目标。播放追上计算时，尚未开始且早于最新目标的准备项被丢弃，避免继续计算已过时的排队目标。跳出准备范围后改取当前目标。
4. 预备投影只在 PSD/RBP 源对象一致时复用；范围投影还要求请求边界一致。用户改范围后，复用匹配窗 bins 并调用既有后端范围投影。源/bin 切换期间不将新范围结果套到旧频率网格。
5. 暂停、seek、时间线拖动、配置变化、关闭面板取消并丢弃准备，旧响应不能恢复过期数值。普通后续窗口等待时保留上一次结果的真实时间和 pending 状态，不把旧结果标为当前时间。

PSD 密度仍来自 PSD，绝对/相对功率及等效 RMS 仍来自 RBP 后端投影；前端不新增积分、Welch 或原始 EEG 科学计算。本次没有后端源码、HTTP/WebSocket、产物格式、数据库、波形绘制、地形图或样式改动。工作区中原有两位小数与细滚动条修改保留。

准备只能移动计算发生时间，不能提高后端实际持续吞吐量。初次冷打开仍需真实计算；2X 或其他负载使生产速度低于消费速度时仍可能滞后。两个未来目标可能产生用户尚未访问的窗口及既有持久化产物；后端资源上限未放宽。取消是协作式的，已进入后端执行的工作可能完成，但其旧结果不能发布到已取消的桌面状态。

## 自动化验证

桌面测试同时完成当前源码构建，使用隔离输出目录，未停止或重启运行中的程序：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --filter 'FullyQualifiedName~Tests.Review&FullyQualifiedName!~Provided_recording' -v quiet --nologo -p:UseSharedCompilation=false -p:OutputPath=D:/AI/Work/brain-platform/_temp/cold-spectral-build/ --logger 'trx;LogFileName=cold-spectral-review.trx' --results-directory D:/AI/Work/brain-platform/_temp/cold-spectral-results
```

结果：422 项，420 通过、2 失败；新增 12 项全部通过，覆盖两未来窗不提前发布、到点复用后端投影、单点/范围/五频段、暂停/seek/拖动/关闭取消、源忽略取消时的晚到隔离、范围修改、串行容量上限、过期排队项丢弃、失败观察及记录末尾。

以下两项使用 `git show HEAD:<path>` 提取修改前生产源码，通过临时 MSBuild Compile overlay 独立重跑，均可在基线复现；未为本次修复改动这些无关行为：

- `Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`：期望 1 次更新，实际 0。
- `Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`：期望暂停，实际仍播放。

基线选中三项用例，1 通过、2 失败。随后当前源码完整重跑得到上述 420/422，避免把基线输出误记为当前验证。依赖/分析器仍有既有 NU1701、xUnit2013、CA2022 警告；构建成功不等于这些兼容性问题已解决。

后端在 `backend` 目录执行：

```powershell
.venv/Scripts/python.exe -m pytest tests/test_custom_frequency_window_api.py tests/test_custom_frequency_inspection.py tests/test_result_scalars.py tests/test_on_demand_rbp_api.py tests/test_on_demand_psd_api.py tests/test_dynamic_window_artifacts.py -q
```

结果：51 通过，72.87 s，2 条既有弃用警告。保持原 PSD/RBP 投影和动态窗口科学契约。

其他检查：严格 OpenSpec 校验通过；`git diff --check` 通过；按项目 `check_file_sizes.py` 规则评估变更的 C#/Python/XAML，均不超过 400 行，无新增文件规模例外。

本地证据：

- `_temp/cold-spectral-final-tests.log`。
- `_temp/cold-spectral-results/cold-spectral-review.trx`。
- `_temp/cold-spectral-baseline-tests.log`。
- `_temp/cold-spectral-baseline-sources/baseline.targets`。
- `_temp/cold-spectral-backend-tests.log`。

上述 `_temp` 为本地验证产物，不作为源码提交。

## 实录验收尚未完成

本次未操作 GUI、删除缓存或运行真实录音冷缓存验收；需要用户运行新版本，在确定未计算过的区段以 1X 连续播放 60–120 s，包含范围修改、暂停恢复、seek 和时间线拖动。记录初次冷打开、稳定播放及跳转恢复三种情况，不能用准备命中的发布耗时代表冷计算变快。2X 另测，不作为 1X 的替代。

启动测试进程前，在同一 PowerShell 会话设置：

```powershell
$env:BRAIN_REVIEW_PERF_DIAGNOSTICS = '1'
$env:BRAIN_REVIEW_PERF_SCENARIO = 'cold-spectral-after-fix'
```

桌面日志仍在 `%LOCALAPPDATA%/BrainPlatform/logs/desktop-YYYYMMDD.log`；后端日志仍在 `storage/logs/backend-YYYYMMDD.log`。对照同一 `review_session_id`：

- `review_spectral_prepare_perf`：未来目标、源和投影耗时、窗口 ID、`publication=false`；这是实际准备成本。
- `review_spectral_pair_perf`：`buffer_state=ready/waiting/miss`、到点后的等待、当前播放位置和真实窗口滞后。
- `review_custom_frequency_apply_perf`：表格最终应用耗时及其真实窗口时间。

验收需确认未来窗不提前显示、旧窗时间可辨、跳转后不回刷、范围/单点数值仍与对应后端源一致，同时观察波形流畅度和长时间负载。当前自动化证据不能证明真实录音已无卡顿，也不能量化同机其他算法竞争后的吞吐量。本次未提交、推送或归档。
