# 回放频段数值面板验证记录

日期：2026-11-06。OpenSpec：`add-review-spectral-values`。

这是首次实现阶段记录。后续标量/空间职责拆分、表格行复用及最新验证结果见[性能修复记录](2026-11-06-review-spectral-performance-fix.md)。

状态：功能实现和自动化专项验证完成；用户真实记录在运行中桌面的联合回放验收尚未执行。本文不构成真实记录、硬件或长时间播放验收。

## 实现范围与数据来源

- 回放页新增“频段数值”入口，打开右侧数值面板。按当前导联顺序展示 RMS 振幅（µV）、绝对功率（µV²）、相对功率（%）和频段平均 PSD（µV²/Hz）。
- 下拉选择现有五个频段：Delta 1–4、Theta 4–8、Alpha 8–13、Beta 13–30、Gamma 30–50 Hz。每个匹配窗口读取全部五频段；切换下拉只选择已有数值，不重新请求分析窗口。
- 使用现有 PSD/RBP adapter、coordinator、dynamic window API 和结果投影，共用回放游标；没有新增 PSD/RBP 科学算法或 Full Run。打开数值面板会驱动两个现有分析 binding，并读取对应投影。
- PSD 直接读取现有 `psd.band-mean-density.<band>` 后端标量投影，边界单位为 V²/Hz；展示转换为 µV²/Hz。禁止从 RBP 功率反推 PSD。
- 绝对功率读取现有 RBP `<band>_absolute_power` 数组（µV²），相对功率读取现有 `<band>` ratio 数组，仅在展示时乘 100。
- 唯一新增的科学数值投影是 `rbp.amplitude.<band>`：调用现有 `band_rms_uv` helper，对同窗已保存的绝对功率开方，得到 RMS 振幅。它不是峰峰值振幅；官方 RBP 数组及其科学版本不变。投影 provenance 为 `rbp-band-rms-amplitude-v1`。
- 现有目录默认窗口为 10 秒，Welch 分段为 4 秒、50% overlap。本轮真实后端合成记录集成测试核验两条现有通路的配置兼容性；面板不会另定窗口或分段设置。
- 原始通道模式的 RBP 分析集使用全部当前通道，保证逐导联表完整；既有趋势选中通道不变。导联模式继续使用当前 montage 输出顺序。

## 一致性与不可用状态

- 合并前验证 recording、游标、实际样本范围、采样率、signal mapping、参考、滤波、Welch/窗口设置、源数据身份及导联顺序。投影另核验窗口 ID、版本、时间范围、字段、单位及通道顺序。
- seek、拖动、配置或 montage 变化清除旧值；revision、取消令牌和当前结果引用校验阻止迟到响应覆盖新窗口。关闭面板释放两个现有 binding。
- 缺失、负数、非有限或非数值功率不转换为零；缺少数组、错误单位或维度返回明确不可用原因。质量 unavailable 或未知时，不显示为有效数值。
- 没有空间坐标、双极导联或地形图点数不足只影响空间绘图；已有有效科学标量仍可显示。PSD/RBP 保留各自质量规则，面板不宣称两者一定满足简单功率等式。
- 后端调整是现有投影字段的增量扩展；现有调用方字段含义和官方结果 schema 不变，无数据库或记录文件迁移。`topomap_inputs.py` 负责结果到标量的适配，`topomap.py` 保留空间投影和服务编排。

## 已执行验证

| 验证范围 | 结果 | 证据与边界 |
| --- | --- | --- |
| 后端相关回归 | 88/88 通过 | 包含原 PSD/RBP API、地形图、RMS 投影、同窗集成及规模策略测试；有 2 个既有 FastAPI/TestClient deprecation warning |
| 桌面专项 | 22/22 通过 | 来源与单位、错窗/版本/导联、质量/缺失、频段切换无新增请求、播放/seek/拖动、取消和迟到响应、montage 变化、真实 WPF 绑定 |
| 桌面隔离构建 | 通过，0 errors | 使用独立输出目录避开运行中桌面 apphost 锁；既有 NU1701 OpenTK/SkiaSharp 和 xUnit analyzer warnings 保留 |
| 全部 Review 测试 | 355/358 通过，3 失败 | 三个失败均在未修改 HEAD 隔离基线复现，见后文 |
| OpenSpec 严格校验 | 通过 | `openspec validate add-review-spectral-values --strict` |
| 差异空白检查 | 通过 | `git diff --check`，并额外检查新增源码空白 |
| 本轮目标代码文件规模评估 | 通过 | 新增生产文件均小于 400 行；超限的既有主 VM/XAML 已更新拆分评估登记 |
| 全局代码规模检查 | 失败，3 项既有问题 | 在未修改 HEAD 隔离基线复现，见后文 |

后端命令（工作目录 `backend`）：

```powershell
.venv/Scripts/python.exe -m pytest tests/test_topomap.py tests/test_psd_topomap_density.py tests/test_rbp_band_rms.py tests/test_rbp_amplitude_projection.py tests/test_review_spectral_sources.py tests/test_rbp_on_demand_capability.py tests/test_psd_on_demand_capability.py tests/test_on_demand_rbp_api.py tests/test_on_demand_psd_api.py tests/test_file_size_policy.py -q --tb=short
```

桌面专项命令（工作目录为仓库根）：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore -o _temp/review-spectral-values-tests --filter 'FullyQualifiedName~ReviewSpectral|FullyQualifiedName~Spectral_panel|FullyQualifiedName~Late_projection|FullyQualifiedName~Closing_while_both|FullyQualifiedName~ReviewAlgorithmRegistrationTests' --logger 'trx;LogFileName=spectral-focused.trx' --results-directory _temp/review-spectral-results --verbosity quiet
```

本机专项及完整 Review 结果分别保存在 `_temp/review-spectral-results/spectral-focused.trx` 和 `_temp/review-spectral-results/review-spectral-values.trx`。这些是忽略目录中的本机验证产物，不纳入源码交付。

### Review 既有失败的基线对照

未修改基线：`aa4a6d6dbce32b2cd6f7853a411e00d17a5a133f`。使用 `git archive HEAD` 导出至 `_temp/review-spectral-baseline`，隔离构建并重跑同名失败项，以下三项均复现：

1. `Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`。
2. `Dragging_exclusively_owns_position_then_restores_previous_playback_state(wasPlaying: True)`。
3. `Provided_recording_exports_through_real_http_client_when_acceptance_is_configured`：真实 HTTP 验收环境未配置，当前 runner 将 xUnit dynamic skip 记为失败。

因此不能宣称全部 Review 回归通过；本轮没有修改这些既有问题。

### 全局代码规模检查的基线对照

`backend/scripts/check_file_sizes.py` 在当前工作树和上述未修改 HEAD 基线上均报告：

- `backend/app/services/algorithm_recording_context.py`：410 行，未登记超限评估。
- `backend/app/services/on_demand_dynamic.py`：558 行，超过登记的 530 行。
- `backend/app/algorithms/psd/runner.py`：464 行，超过登记的 420 行。

这些文件不属于本轮修改范围。本轮将原 577 行的 `topomap.py` 按职责拆为 332 行的编排模块和 280 行的输入适配模块，并移除其旧规模例外。

### 离屏 WPF 目视检查

使用合成 fixture 渲染并检查 `_temp/review-spectral-values-tests/_temp/review-spectral-panel.png`：Theta 下拉显示“波 · 4–8 Hz”标签；F3 显示 RMS 2、绝对功率 4、相对功率 25、PSD 2，F7 对应 3、9、50、3。四列为只读数值表格。

这只验证该合成输入下的 WPF 呈现与绑定，不代表用户真实记录在运行中回放页完成验收。

## 待人工验收与运行风险

- 尚未在运行中桌面选取用户真实记录，对照 PSD/RBP 地形图与四列数值执行联合验收；需实际检查导联选择、五频段、播放/暂停、拖动、边界窗口、换 montage 和不可用状态。
- 打开面板同时驱动两个已有分析 adapter，分析工作量高于仅打开一个算法；本轮未执行真实记录长时间播放或压力测试。
- 自动化验证确认数据来源、契约和生命周期，不是独立科学算法重算审计，也不改变既有 PSD/RBP 质量策略。
- 本轮未提交、推送或归档 OpenSpec。
