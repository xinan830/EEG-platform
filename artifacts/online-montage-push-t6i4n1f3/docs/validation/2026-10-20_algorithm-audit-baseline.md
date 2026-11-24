# 九个官方算法审计基线

生成日期：2026-10-20<br>
身份来源：`backend/app/algorithms/builtin_modules.py` 及各模块 `manifest.py`。<br>
本文件是 OpenSpec change `audit-official-algorithms-end-to-end` 的阶段 0 产物，不代表临床或诊断有效性。

## 运行身份矩阵

| ID | 模块 | scientific version | implementation identity | 模式 | 输出主单位 | 运行状态 | 备注 |
|---|---|---|---|---|---|---|---|
| `psd` | `PsdAlgorithm` | `offline-spectral-v3` | `psd-runtime-v3-explicit-filters` | static/dynamic | PSD `V²/Hz`，频率 `Hz`，band share `ratio` | runnable | 通用频谱原语 |
| `stft` | `StftAlgorithm` | `spectrogram-v2` | `stft-runtime-v4-relative-window-times` | static/dynamic | `V²/Hz`、显示 `dB re 1 µV²/Hz` | runnable | 时频矩阵；内部时间为窗口相对中心轴 |
| `peak_frequency` | `PeakFrequencyAlgorithm` | `official-peak-frequency-v1` | `peak-frequency-runtime-v2-frequency-contract` | static/dynamic | `Hz` | runnable | 选择声明频段内最大 PSD bin |
| `iapf` | `IapfAlgorithm` | `official-iapf-v2` | `iapf-runtime-v3` | static/dynamic | `Hz` | runnable | 1/f 校正后的 Alpha peak/COG |
| `band_ratio` | `BandRatioAlgorithm` | `official-band-ratio-v1` | `band-ratio-runtime-v2-frequency-contract` | static/dynamic | `ratio` | runnable | 边界插值梯形积分后相除 |
| `theta_beta` | `ThetaBetaAlgorithm` | `official-theta-beta-v2` | `theta-beta-runtime-v1` | static/dynamic | `dimensionless` | runnable | 依赖当前窗 IAPF 的个体化频段 |
| `rbp` | `RbpAlgorithm` | `official-rbp-v2` | `rbp-runtime-v2-five-band` | static/dynamic | 五频段 `ratio` | runnable | 1–50 Hz 五频段归一化；保留历史四频段版本 |
| `faa` | `FaaAlgorithm` | `official-faa-v2` | `faa-runtime-v2` | static/dynamic | `dimensionless` | runnable | 显式 F3/F4 成对 epoch |
| `brainbeat` | `BrainbeatAlgorithm` | `official-brainbeat-v1` | `brainbeat-runtime-v1` | static/dynamic | `ratio` | runnable | 显式 Fz/Pz；离线逐窗 IAPF；不等同实时 EMA 链路 |

`brainbeat` 的 `required_channel_roles` 是 `Fz/Pz/IAPF`，配置还强制两个来源不同且角色明确；FAA 的左右来源通过配置字段而不是固定通道顺序解析。所有九个模块由同一 built-in registry 提供给 runtime，但科学公式和算法专属 evidence 仍由各模块所有。

## 已有证据映射

已有独立或影子验证主要集中在：PSD/STFT 频谱原语、RBP 五频段积分、IAPF 1/f 合成谱、FAA paired-epoch FFT、Theta/Beta/Brainbeat 组合指标。对应文件包括：

- `backend/tests/test_official_algorithm_independent_references.py`
- `backend/tests/test_official_algorithm_shadows.py`
- `backend/tests/test_peak_frequency_band_ratio.py`
- `backend/tests/test_iapf_algorithm_module.py`
- `backend/tests/test_theta_beta_v2.py`
- `backend/tests/test_rbp_five_band_contract.py`
- `backend/tests/test_faa_continuous.py`
- `backend/tests/test_brainbeat_algorithm.py`
- `backend/tests/test_psd_frequency_contract.py`
- `backend/tests/test_stft_frequency_ranges.py`

2026-10-20 已运行相关后端测试集合：**83 passed**。该结果证明当前测试样例与已有工程契约一致；它不等于真实 EEG 独立验证或临床有效性。

## 当前基线判断

- 已确认：九个模块都有稳定身份、静态/动态入口、显式输出 schema 和结构化质量/失败路径；科学计算在后端，前端不负责重新计算。
- 已确认：PSD、STFT、RBP、FAA、IAPF 等已有一定独立数值或影子证据；动态窗口测试覆盖了部分 warm-up 和失败继续语义。
- 待验证：真实记录上的逐点独立复算；所有算法统一的样本边界/单位/参考证据；IAPF 拟合点不足、非正/非有限 PSD 和退化拟合；Theta/Beta 相对频段靠近边界时的行为；Peak Frequency、Band Ratio、Brainbeat 的完整原始输入独立复算。
- 科学边界：离线 Brainbeat 的逐窗计算不能为实时 BrainBeat 的 IAPF-lock/EMA 状态性行为背书；工程测试通过不能推出临床结论。

## 生成物

- 机器可读身份快照：`docs/validation/2026-10-20_algorithm-audit-baseline.json`
- 审计执行计划：`docs/validation/algorithm-audit-plan-2026-10-20.md`
- OpenSpec：`openspec/changes/audit-official-algorithms-end-to-end/`
