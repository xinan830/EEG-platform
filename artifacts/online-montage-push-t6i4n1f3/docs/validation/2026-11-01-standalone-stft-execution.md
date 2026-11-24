# 独立 STFT 统一执行及有限上下文验证（2026-11-01）

## 修复范围与根因

变更：`unify-standalone-stft-execution`。此前独立时频页虽已进入平台统一执行通道，科学源仍先创建整段录音的连续滤波缓存，导致短窗口依赖整段准备。此次将 STFT 源准备改为目标区间加有限上下文；删除该算法的整段准备路径，不增加旧执行器或结果转换。

静态、全量动态和按需计算继续共用 `stft/frame.py`，复用平台生命周期、资源准入、缓存和发布。读取/滤波/裁剪由 `bounded_window_preprocessing.py` 拥有，STFT 源组装由 `spectrogram_source.py` 拥有；TNK 使用共享操作但仍明确声明原有两秒上下文和科学版本。Review 接入、实时采集和此前工作区的 RBP/Review 改动不在本次范围。

当前科学 `spectrogram-v3-bounded`，预处理 `spectral-window-v1`，实现 `stft-runtime-v6-bounded`，窗口策略 `stft-dynamic-window-v3-bounded`，Definition 1.1.0。新身份和证据校验拒绝旧整段结果复用；原始录音与历史 artifact 不改写。

## 科学契约

- 每侧上下文为 `ceil(max(2, 3 / low_hz) * sfreq)` 样本，读取区间裁到录音边界，仅读取选择的通道。
- 按原有陷波、四阶零相位 Butterworth 带通顺序处理读取块，再裁出目标样本。目标和上下文缺样均拒绝，不插值。
- 保留 Welch 总体质量门、4 s Hann / 1 s 内步长及内部质量规则。静态保留合格内部行；动态外窗任一内部行被拒绝则不发布可用矩阵，全量保留槽位与 NaN，按需返回失败证据。
- 内部信号为 sample-major float64 V；服务线性输出为 µV²/Hz，算法数组为 V²/Hz，dB 为 dB re 1 µV²/Hz。源中心时间为录音绝对时间，动态内部轴为窗口相对时间。
- 同一源、配置和目标样本边界的三种策略必须一致；不同外层区间的重叠部分可能不同。这是科学契约变更，不能声称与旧整段滤波逐点等价。
- 预处理证据含实际读取/目标样本范围、上下文、零相位、边界截断、单位、通道顺序及缺样策略。后端帧校验及 WPF 消费均要求当前契约。

## 自动验证

Python 命令从 `backend/` 执行，使用项目虚拟环境；桌面和 OpenSpec 命令从仓库根目录执行。

```powershell
.\.venv\Scripts\python.exe -m pytest tests/test_stft_execution_parity.py tests/test_stft_on_demand_capability.py tests/test_stft_frequency_ranges.py tests/test_stft_offline_reuse.py tests/test_stft_bounded_preprocessing.py tests/test_spectrum_service.py tests/test_spectrum_api.py tests/test_signal_source_contract.py tests/test_on_demand_registry.py tests/test_algorithms_catalog_api.py tests/test_tnk_window_preprocessing.py tests/test_scientific_compute_coordinator.py -q
```

146 项通过。覆盖三策略矩阵/单位/请求/证据一致、频段及独立 SciPy periodogram 数值参考、通道顺序、坏窗恢复、非法坐标/矩阵、预热、取消、参数校验、源关闭、旧缓存隔离和 TNK 两秒上下文独立数值参考。

模拟 24 小时、200 Hz 录音，只按请求生成样本，不分配整段数组。5 s 目标在低截止 1 Hz 时最多读取 11 s；起始窗口读取 8 s，尾窗口读取 8 s。低截止 0.5 Hz 时内部目标读取 17 s，8 Hz 时读取 9 s。断言禁止调用整段预处理方法，也不产生整段科学缓存。这证明请求范围有界，不能当成真实长文件性能测量。

```powershell
.\.venv\Scripts\python.exe -m pytest tests/test_official_directional_reuse.py tests/test_official_combined_strategies.py tests/test_official_algorithm_runs.py tests/test_official_algorithm_boundary_conditions.py tests/test_official_algorithm_extended_audit.py -k stft -q
```

13 项通过，93 项未选中。后端本次选定范围合计 159 项通过；有既有 Starlette/httpx/anyio 弃用警告。

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --filter "FullyQualifiedName~Stft|FullyQualifiedName~OfficialCombinedStrategyTests|FullyQualifiedName~Full_stft_rejects_old_whole_record_preprocessing_contract" -o .isolated-build/stft-bounded --verbosity minimal
```

50 项通过，WPF/BAML 编译成功。覆盖按需与全量 STFT 消费、质量/失败帧、清理旧图、坐标和旧整段契约拒绝。现有警告包括 OpenTK/OpenTK.GLWpfControl/SkiaSharp 的 NU1701、预览 SDK 提示和 xUnit2013。

## 有限上下文相对旧整段滤波的差异

独立参考使用 SciPy iirnotch(Q=30)、filtfilt、四阶 butter/sosfiltfilt、periodogram(Hann、constant detrend、density)，验证有限上下文实现 `rtol=1e-11, atol=1e-24`。另对比整段 200 s 的合成信号：200 Hz、10 µV 的 10 Hz 正弦加 3 µV 的 1.3 Hz 正弦，5 s 目标，50 Hz 陷波、带通上限 30 Hz。

| 目标起点 s | 低截止 Hz | 最大绝对差 V²/Hz | 相对 L2 差 |
| --- | --- | --- | --- |
| 0 | 0.5 | 1.092326404e-16 | 1.016040282e-6 |
| 0 | 1 | 4.551909947e-16 | 3.031691750e-6 |
| 100 | 0.5 | 1.137115217e-16 | 9.905690284e-7 |
| 100 | 1 | 4.562531713e-16 | 3.084213576e-6 |
| 195 | 0.5 | 1.926039078e-16 | 1.558332464e-6 |
| 195 | 1 | 1.799897215e-16 | 9.936952446e-7 |

这些数字仅描述该合成信号，不是实际 EEG 的误差界限或临床验证。上下文长度是可复现策略，不保证所有波形的滤波瞬态都在该范围内消失。

## 文件规模与检查

本次新建生产文件均低于 400 行：bounded_window_preprocessing 88、spectrogram_source 153、STFT frame 178；runner 182、window 78、evidence 50、manifest 50。spectral_analysis 从原 562 行降至 485 行，保持 550 行登记限额，并更新职责拆分评估。

全仓规模检查仍有一个既有失败：`backend/app/services/on_demand_dynamic.py` 为 559 行，超过登记 530；本次未修改该文件或提高限额。全局规模检查不能报告为通过。`git diff --check` 和 `openspec validate unify-standalone-stft-execution --strict` 均通过。

## 人工验收与限制

尚未运行真实 WPF 点击、实际长录音首窗延迟/内存或播放流畅度验收。自动测试证明短窗不调用整段科学准备；文件打开、原生 reader 元数据及实际磁盘性能仍需实测。显式请求很长的静态分析区间仍与该请求长度成比例。

重启后端，使用本次重新构建的 WPF，进入独立“时频分析”页：

1. 选择长录音、一个通道，动态按需使用 5 s 窗口、低截止 1 Hz，观察首窗是否无需等待整段准备。
2. 播放并定位到开头、中间、结尾；确认时间轴、热图和失败信息对应当前窗口，旧结果不覆盖新定位。
3. 用相同通道、滤波、目标范围分别运行静态和全量动态，比较同一窗口；不同外层边界不作为逐点相等的验收前提。
4. 修改频段/通道后重新计算，确认旧缓存不混入。坏窗仍显示真实失败，后续好窗独立恢复；不套用 RBP 质量警告策略。

未提交、未推送，未同步主规格或归档本变更。
