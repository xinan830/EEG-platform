# TNK 生物反馈算法源码盘点

本账本记录从 ANT-Amplify `eeg-process-main` 当前可执行源码和聚焦测试中识别出的科学计算与数值输出，用于迁移前确认范围。它是**源码盘点**，不是已经冻结的科学定义；具体公式参数、输入角色、窗口、状态和错误语义由 OpenSpec 任务 1.2 逐项冻结。

## 纳入的科学计算/结果候选

| 编号 | 科学计算或结果 | 可执行源码证据 | 聚焦测试证据 | 盘点结论 |
|---|---|---|---|---|
| 1 | IAPF（个体 Alpha 峰频） | `ANT-Amplify/eeg-process-main/src/processing/iapf_estimator.py`：`IAPFEstimator.compute`、`_fit_spectrum`、`_select_alpha_peak`、`_alpha_center_of_gravity`；`src/processing/processor.py`：`try_lock_iapf` | `tests/test_iapf_estimator.py`、`tests/test_processor_iapf.py` | 独立科学结果候选；滚动锁定与候选状态属于其结果语义，后续须冻结离线重放方式。 |
| 2 | 放松度 | `src/processing/processor.py`：`EEGProcessor.calculate_metrics` 中后枕 IAPF 窄 Alpha 相对总功率计算和分数映射 | `tests/test_processor_iapf.py`（窄 Alpha 行为、指标输出） | 可执行、可见的数值指标。 |
| 3 | 空间分布 | `src/processing/processor.py`：`calculate_metrics` 中额区/后枕区 Alpha 相对功率比及有效性标记 | `tests/test_processor_iapf.py`（空间分布比值和有效性行为） | 可执行、可见的数值指标；依赖额区与后枕区数据。 |
| 4 | 节律稳定性 | `src/processing/processor.py`：`calculate_metrics` 中逐通道窄 Alpha 历史 CV、加权及就绪状态 | `tests/test_processor_iapf.py`（稳定性历史、权重、冷启动/就绪行为） | 可执行、带历史状态的数值指标。 |
| 5 | 综合冥想指数 | `src/app/eeg_biofeedback.py`：`composite_weights`、`compute_composite_score`；界面状态更新调用该聚合 | `tests/test_composite_score.py` | 有公式和独立单元测试，应纳入数值结果范围。启动随机种子、展示平滑与趋势统计属于另外的 UI/展示行为，不自动视为科学算法。 |
| 6 | BrainBeat（原始谱） | `src/processing/processor.py`：`_calculate_brainbeat`、`_calculate_brainbeat_epoch_powers`、`segment_brainbeat` | `tests/test_band_metrics.py`、`tests/test_processor_iapf.py` | 存在逐窗状态化结果和整段报告计算；两者计算路径需在 1.2 明确是同一身份下的输出模式还是独立结果契约。 |
| 7 | BrainBeat Flat（去 1/f 残差谱） | `src/processing/processor.py`：`residual_spectrum`、`_calculate_brainbeat_flat`、`_calculate_brainbeat_flat_epoch_powers` | `tests/test_brainbeat_flat.py` | 独立于原始谱 BrainBeat 的数值路径；有单独 warm-up/EMA 状态。 |
| 8 | 疲劳指数 | `src/processing/processor.py`：`_calculate_fatigue_index`，使用逐通道频带功率与历史平滑状态 | `tests/test_fatigue_index.py` | 可执行、逐通道、带状态的数值指标。 |
| 9 | HAI（快慢波比） | `src/processing/processor.py`：`compute_hai`、`_derive_hai`；由 IAPF 估计结果中的逐通道原始 PSD 派生 | `tests/test_hai_index.py` | 有独立公式和数值测试；当前生产链复用 IAPF 路径的 PSD 与更新节拍。 |
| 10 | FAA（额叶 Alpha 不对称） | `src/processing/processor.py`：`compute_faa`、`compute_faa_report` | `tests/test_faa.py` | 可执行的分段/事件区间结果，不属于逐窗实时指标路径。 |
| 11 | 固定频段 RBP | `src/processing/processor.py`：`band_power`、`band_relative_power`、`_compute_rbp_frame` | `tests/test_band_metrics.py`、`tests/test_rbp.py` | 可执行、逐通道 δ/θ/α/β 相对功率结果。 |
| 12 | 1/f 非周期指数（界面“皮质兴奋/抑制”数值） | `src/processing/iapf_estimator.py`：频谱拟合结果；`src/processing/processor.py`：`_derive_aperiodic`、`estimate_aperiodic_exponents` | `tests/test_aperiodic_exponent.py` | 有独立可见数值输出，但复用 IAPF 的同一拟合/谱路径；独立模块身份与 IAPF 多输出的边界由 1.2 冻结。 |

## 明确排除

- **残差谱 RBP / “去 1/f RBP”**：当前 `processor.py` 没有该 RBP 计算或 payload；`tests/test_rbp_ui_wiring.py::test_rbp_flat_panel_and_payload_removed` 明确断言相关面板、处理函数和 payload 已删除。`residual_spectrum` 当前服务于 BrainBeat Flat，不能据此推断存在残差 RBP。
- 波形预览、采集源、设备控制、BDF 录制、IPC、图表组件、显示颜色/坐标轴、趋势统计和启动随机分数：这些是基础设施或表现层行为，不是本次迁移的独立科学计算模块。

## 迁移边界提示

- ANT 源码的计算集中在 `src/processing/processor.py` 和 `src/processing/iapf_estimator.py`；综合指数聚合目前位于 `src/app/eeg_biofeedback.py`。这说明现有科学逻辑有一部分放在应用层，移植时应提取数值契约到后端 TNK 模块，不照搬 PyQt 结构。
- IAPF、HAI 和 1/f 指数在 ANT 中共享频谱估计路径，但数值输出及失败含义不同。共享输入/谱证据不等于必须合成一个算法模块；最终模块边界应由各自配置、通道角色、策略能力和结果契约决定。
- BrainBeat 同时存在逐窗动态和时段报告入口。账本先记录两条可执行计算路径；数值参数及其是否属于一个结果契约将在 1.2 冻结。
- 该盘点不声称任何算法具有临床有效性，也不声称运行时策略等价已验证。

## 1.2 科学契约冻结进度

下表记录目前能从执行源码直接确认的契约。它是迁移输入基线，不代表 ANT 实时 UI 的平滑/展示行为都应搬入平台。目标参考环境冻结为 Windows x64、Python 3.10.20、NumPy 2.2.6、SciPy 1.15.3、MNE 1.12.1、specparam 2.0.0rc6；这些版本在 `ANT-Amplify/eeg-process-main/.venv` 实际导入确认，聚焦测试在同一环境通过。源码允许回退到 FOOOF，但该参考环境选择 specparam；缺少 specparam 的环境会让拟合结果不可用。其他锁文件解析环境（Python ≥3.11）不是本次参考基线。

| 候选结果 | 当前已确认的数学/输入契约 | 窗口、状态、失败语义与尚待冻结点 |
|---|---|---|
| IAPF | 输入 EEG 电压（V）；默认后部 ROI Pz/Oz 平均 PSD，Fz 单独提供额区谱；50 Hz 陷波、1–30 Hz 带通；MNE Welch 4 s Hann、50% 重叠。specparam 在 3–30 Hz 拟合，7–13 Hz 搜峰；峰 prominence、模型 R²/MAE 门及 CoG 回退选择峰中心或残差质心。 | 最近 30 s，5 s 尝试；干净比例至少 0.75，伪迹窗 2 s、±150 µV，短于 4 s 的干净片段并入 BAD；连续 3 个合格候选的中位数锁定。无效估计给出未校准/失败原因，处理器保留先前 live/global。离线 reset 起点、锁定后 live 更新策略及明确阈值容差尚需转成平台可复现契约。 |
| 放松度 | 每秒 4 s 滤波窗 Welch（2 s Hann 段、50% overlap）；后部 ROI Pz/Oz 上 [IAPF−1,IAPF+1] 功率和除以 [1,30] 功率，再 logistic(center=0.15,slope=0.055) 映射至 0–100。 | IAPF 未锁定时使用初始 10 Hz；频率带点数不足/总功率无效时当前逻辑输出 0 比例。仅实际存在的 Pz/Oz 参与。源代码未给出显式质量拒绝门；平台对缺通道/无有效功率的结构化失败语义待定。 |
| 空间分布 | Fz 的 IAPF Alpha 相对 [1,30] 功率，除以 Pz/Oz 后部平均 PSD 的相同 Alpha 相对功率；再 logistic(center=0.45,slope=0.15) 映射至 0–100。后部相对功率低于 0.02 时标记 `_sd_valid=false`。 | 每秒 4 s Welch 窗；频带为 [IAPF−2,IAPF+2]，未锁定默认 IAPF=10 Hz。缺 Fz 或全部后部通道、分母无效、sd_valid=false 的具体平台结果状态需冻结，不能只复制当前 UI 的 0 值。 |
| 节律稳定性 | Fz/Pz/Oz 各自计算 [IAPF−1,IAPF+1] / [1,30] 相对功率；每通道最近 10 帧 CV，映射 `1/(1+CV)`，按 Fz=0.2、Pz=0.4、Oz=0.4 在可用通道间重归一后线性合成，再 clip_map([0.40,0.90]) 至 0–100。 | 每通道至少 8 个样本才 ready；未 ready 给中性 0.65（映射 50 分）。缺失通道不更新其历史；当前存在通道决定 ready。窗口边界/历史 reset 和样本不足时 quality 状态需冻结。 |
| 综合冥想指数 | `compute_composite_score`：稳定性 ready 且 SD 有效时 0.8×稳定性+0.2×空间分布；只有稳定性 ready 时取稳定性；否则取空间分布（100%）。 | `test_composite_score.py::test_weights_sd_fallback_when_stability_not_ready` 明确规定稳定性未 ready 且 SD 无效时仍选 SD；这是源行为，迁移应保留该数值选择并把 `_sd_valid` 作为独立质量证据传出，避免把无效 SD 误标为有效。median(最近5)、追随步进、展示 EMA 和随机启动分数属于 UI/展示路径，不是该聚合函数。 |
| BrainBeat | Fz theta 相对功率 / Pz IAPF-Alpha 相对功率；theta=[max(4,IAPF−6), IAPF−2]，alpha=[IAPF−2,IAPF+2]，归一化分母各自 [1,30]；每个 4 s 输入窗按 2 s Hann、50% overlap Welch。 | Fz/Pz 任一样本绝对值超过 150 µV 则本窗拒绝并沿用上次；log 比值先 3 帧均值初始化，之后 log-EMA α=0.15，再 exp。流更新周期 1 s；冷启动及缺谱状态需要平台显式表示，不能把沿用旧值误记为新结果。另有按时段报告的 `segment_brainbeat`：固定传入 IAPF、theta/alpha 频带、1–30 Hz 总功率归一；它与逐窗 EMA 不是同一输出契约，边界待决定。 |
| BrainBeat Flat | 与 BrainBeat 相同的 Fz/Pz 角色、IAPF 动态频带和 4 s/2 s Hann Welch；每通道自身 PSD 在 3–30 Hz 做非周期拟合，按 `clip(PSD−10^(offset−exponent·log10(f)),0)` 得残差，再用残差 [1,30] 归一化并算 theta/alpha 比值。 | 继承 150 µV 门；任一拟合不可用时沿用上次；独立于 BrainBeat 的 3 帧 log warm-up 和 α=0.15 EMA。源代码没有对残差分母为零构造单独 failure payload，迁移需明确处理。 |
| 疲劳指数 | Fz/Pz/Oz 逐通道 Theta/Beta 功率比；theta=[max(4,IAPF−6),IAPF−2]，beta=[IAPF+2,30]；同一每秒 4 s Welch PSD。 | 每通道独立 3 帧 log warm-up、α=0.15 EMA 后 exp；缺通道不更新/保留上次；任一频带功率非正则跳过更新。IAPF 初始 10 Hz。要冻结无历史、频带被滤波设置截断时的状态。 |
| HAI | 对 IAPF 路径同一 30 s 清洁 PSD 的逐通道原始谱计算 `log10(P[13,25]/P[1,8])`，不去 1/f；角色取实际可用 Fz/Pz/Oz。 | 与 IAPF 同 5 s 节拍、伪迹门和预处理；IAPF 选峰门失败不阻止 HAI，只要 PSD 存在。任一带积分非正时不更新并保留上值；迁移结果需明确区分当前有效值与 stale 值。 |
| FAA | F3/F4；已滤波信号上 `ln(P_F4[8,13]/P_F3[8,13])`。先每通道去均值，再 2 s Hann epoch、50% overlap；超过 ±150 µV 的 epoch 对两通道成对剔除；干净 epoch 在线性 PSD 域平均，单边密度标定后对 8–13 Hz 梯形积分。 | 事件时段结束才算；开头丢弃 12 s；至少 10 个干净 epoch；时段缓冲上限 1800 s。失败原因包括 too_short、too_few_clean_epochs、band_truncated、non_finite。实时滤波为 50 Hz notch + 4 阶 Butterworth 1–30 Hz 因果 SOS；时段分析使用同系数零相位 sosfiltfilt。 |
| 固定频段 RBP | Fz/Pz/Oz 各自对滤波后 4 s buffer 去均值，Welch 2 s Hann、50% overlap；δ[1,4]、θ[4,8]、α[8,13]、β[13,30] 梯形积分后分别除四带功率之和。EEG 输入单位 V，比例无量纲。 | 仅在 RBP 面板启用时计算；频带积分至少需两个频点，分母非有限或≤0则该通道 `invalid_denominator`；非 Fz/Pz/Oz/不可用通道 `non_eeg_or_unavailable`。不同 ROI/静态计算能力待配置契约冻结。 |
| 1/f 非周期指数 | IAPF 路径逐通道谱拟合指数；另有 Pz+Oz 先平均 PSD 再拟合的 pooled 值。频率拟合 3–30 Hz；`aperiodic_exponent` 为 specparam 非周期指数，offset 是另一项证据值，不单独注册。 | 复用 IAPF 的 30 s、5 s、滤波与伪迹处理；不叠额外 EMA。拟合字段不可用时通道沿用上次有效值，IAPF 峰选择门失败不影响该拟合输出。按通道输出与 pooled 输出是否同一个算法结果契约需在实现前决定。 |

### 聚焦测试核对结果

以下为阅读聚焦测试断言得到的行为证据；测试执行仍受 ANT 环境缺少 MNE/Qt 阻碍，不能把静态审阅描述成通过测试运行。

| 范围 | 测试确认的边界或精确行为 |
|---|---|
| IAPF | `test_iapf_estimator.py` 覆盖峰优先、弱峰回退 CoG、R²/质量比例失败、无后部 ROI、4 s/50% Hann Welch 与 0.25 Hz 分辨率，以及不足 4 s 的干净片段吸收；`test_processor_iapf.py` 覆盖 5 s 尝试/候选节拍、3 个候选中位数锁定、失败窗口不清除已有候选、reset 清候选。测试中的宽松参数仅为合成信号可测性调整，生产默认仍按 estimator 构造参数。
| 放松度、空间分布、稳定性 | `test_processor_iapf.py` 明确后枕 ROI 仅 Pz/Oz，空间分布使用“额区相对功率 / 后部平均 PSD 相对功率”，稳定性权重为 Fz/Pz/Oz = 0.2/0.4/0.4。当前无效或缺输入时稳定性用中性分数、SD 标记有效性，须保持数值与质量字段分离。
| 综合冥想指数 | `test_composite_score.py` 明确 80/20、稳定性单独兜底、SD 单独兜底；冷启动即使 SD 无效也选 SD。函数本身只做权重选择和加权，不做 median、惯性追随或随机初始化。
| BrainBeat Flat / Fatigue | 两者测试证明有独立于原始 BrainBeat 的计算/EMA 状态并由 `reset_buffers()` 清空；Fatigue 在 3 帧 warm-up 前不出值，首个锁定值等于 warm-up log 比值均值的 exp，通道限定 Fz/Pz/Oz 并受计算通道选择影响。Flat 拟合参数无效返回 None，生产路径在失败时保留上值。
| HAI / 1/f | 测试证明二者复用 IAPF 每 5 s 同一 PSD 路径，不做自身滤波/PSD 或额外平滑；低质量窗保留先前值，reset 清空值历史。HAI 固定 [1,8] 和 [13,25]，不是 IAPF 相对频段；pooled 1/f 是平均 Pz/Oz PSD 后拟合，不是两个通道指数的算术平均。
| FAA | `test_faa.py` 覆盖 epoch 尾部丢弃、固定 8–13 Hz、成对剔除、幅度阈值、短数据/干净 epoch 不足/窄滤波截断失败、缺 F3/F4 禁用、period reset 和 4 个预设事件对。输入需在上游已滤波；报告阶段还会做开头 12 s 丢弃。
| RBP | `test_rbp.py` 明确只输出 Fz/Pz/Oz，四个固定边界且比例和为 1；未启用时 None，活动时不依赖 IAPF 校准结果；不可用通道和分母/短数据均输出结构化 invalid_channels。 |

### 契约冻结决策

- **12 个结果候选对应 12 个独立 TNK algorithm ID。** 指标有不同通道角色、状态、更新节拍或失败语义；不能把它们打包成一个脑指标科学算法。算法列表可统一分组展示，但分组不是算法身份。
- **IAPF 输出可携带辅助频谱证据，HAI 和 1/f 保持各自算法身份。** 它们共享源 PSD 是输入复用关系，不代表同一结果；各模块可独立选择执行，平台运行时仅按显式数据依赖共享计算上下文。
- **BrainBeat 单一身份含两种明确结果形态。** 窗口流形态使用 warm-up/EMA，时段报告形态输出原始谱 ratio；报告不能冒充窗口 EMA 值。它们的数值入口可在一个模块内用显式 `result_mode` 区分，输出契约/策略声明分别冻结，不增加第二个同名 ID。
- **aperiodic 指数作为一个模块结果包含逐通道和 pooled 两组输出。** pooled 定义为 Pz/Oz 平均谱后拟合；offset 是伴随证据字段，不是独立结果模块。
- **Composite Meditation Index 保留源公式，即使输入 SD 当前无效且稳定性未就绪仍以 SD 兜底。** 输出必须同时带权重及来源指标的质量证据，状态标为 degraded/invalid-input，不能以正常有效分数呈现；这是在保留 ANT 数值公式的同时遵守平台结构化质量契约。
- **任何“沿用上次值”不作为新鲜科学结果输出。** 迁移模块应返回当前请求的显式 unavailable/held 状态与可追溯的 last-valid 证据（如契约允许），不能将旧数值伪装为本窗刚计算出的结果。

### 1.3 与现有平台算法的边界核对

平台当前 builtin composition list 仍注册 9 个既有 ID：`band_ratio`、`brainbeat`、`faa`、`iapf`、`peak_frequency`、`psd`、`rbp`、`stft`、`theta_beta`；manifest 声明的当前版本分别为 `official-band-ratio-v1`、`official-brainbeat-v1`、`official-faa-v2`、`official-iapf-v2`、`official-peak-frequency-v1`、`offline-spectral-v3`、`official-rbp-v2`、`spectrogram-v2`、`official-theta-beta-v2`。唯一注册组合点是 `backend/app/algorithms/builtin_modules.py`。TNK 候选使用独立 `tnk_*` ID，不替换或别名到这些条目。

| 同名/近似结果 | 平台当前定义 | TNK/ANT 差异 | 边界结论 |
|---|---|---|---|
| IAPF | 对平台选定通道谱作 1/f 对数线性残差，输出 peak/CoG | ANT 默认 Pz/Oz 后部 ROI 聚合，specparam 固定+gaussian 模型及峰/质量门；另有 30 s 滚动输入和候选锁定 | 同名不是等价公式；TNK 独立 ID。 |
| BrainBeat | Fz/Pz 独立窗口比率；manifest 明确 `cross_window_ema=false` | ANT 每秒结果带 3 帧 log warm-up 与 EMA；另有时段原始谱报告 | 不替换平台现有 window-local 算法。 |
| FAA | F3/F4 paired epoch 与 8–13 Hz 对数功率差；当前 manifest `official-faa-v2` | 公式方向与频段相近，但 ANT 另含开头 12 s 丢弃、事件时段输入、报告/epoch 门及上游预处理约定 | 共享公式形状不代表相同输入/状态语义；保留独立 ID。 |
| RBP | 五频带 Delta/Theta/Alpha/Beta/Gamma，1–50 Hz 分母 | ANT 候选四频带 δ/θ/α/β，以 1–30 Hz 分母且只允许 Fz/Pz/Oz | 数值定义明显不同；保留独立 ID。 |
| Theta/Beta、Band Ratio | 平台通道配置及其声明频带公式 | ANT 疲劳指标按 Fz/Pz/Oz 逐通道计算且带 3 帧 warm-up/EMA；HAI 固定 1–8 与 13–25 Hz 的 log10 比值 | 不可把 TNK 疲劳或 HAI 映射为现有比率 ID。 |

验证边界：本轮 `git status/diff` 确认 `backend/app/algorithms/` 和 `backend/app/scientific/` 生产实现无工作区差异；目录契约测试及科学契约测试通过（12 passed）。但冻结 Run/artifact 对照 `backend/tests/test_result_boundary_baseline.py` 为 34 failed、14 passed：已见失败包含 PSD/幅度 provenance/evidence 的极小浮点差异被 helper 按精确相等断言（例如 0.002956470735703076 vs 0.0029564707357032883），不能据此断言科学公式变化，也不能据此宣称结果基线全部保持。该基线失败需要单独定位其快照/比较契约；在解决前 OpenSpec 1.3 不勾选。本段不授权修改冻结基线或放宽比较器。

### 1.4 参考实现环境与解析夹具冻结

#### 参考环境身份

| 字段 | 冻结值 | 来源/验证 |
|---|---|---|
| OS / 架构 | Windows x64 / AMD64 | ANT 项目要求环境及当前 `.venv` 平台。 |
| Python | CPython 3.10.20 x64 | `ANT-Amplify/eeg-process-main/.python-version` 指定 3.10；该 `.venv` 实际 `sys.version` 为 3.10.20。 |
| NumPy / SciPy | 2.2.6 / 1.15.3 | ANT `uv.lock` 的 Python <3.11 分支，且当前虚拟环境 import 版本一致。 |
| MNE / specparam | 1.12.1 / 2.0.0rc6 | ANT `uv.lock` 与当前虚拟环境 import 版本一致。参考运行强制使用 specparam，不允许静默切换 FOOOF。 |
| 其他数值后端 | Windows x64 NumPy/SciPy wheel；BLAS/LAPACK 运行时须由 fixture manifest 记录实际信息 | 依赖 wheel hash 由 `uv.lock` 固定；目前尚未登记本机 BLAS 实际实现，reference runner 必须输出该信息，比较报告附环境指纹。 |
| 参考源码身份 | `ANT-Amplify/eeg-process-main/src/processing/{iapf_estimator.py,processor.py}` 与 `src/app/eeg_biofeedback.py`；算法相关定义见本账本 | 上游工作树/提交摘要须在首个 reference artifact manifest 记录，禁止只记录可变分支名。 |

ANT 聚焦源测试命令：`uv run --locked --extra dev -- python -m pytest tests/test_iapf_estimator.py tests/test_processor_iapf.py tests/test_composite_score.py tests/test_band_metrics.py tests/test_brainbeat_flat.py tests/test_fatigue_index.py tests/test_hai_index.py tests/test_faa.py tests/test_rbp.py tests/test_aperiodic_exponent.py -q`。本次执行结果为 200 passed、2 条短输入 MNE 滤波长度警告。该环境只作为迁移参考计算身份；backend 自身依赖与锁文件仍以平台项目定义为准，不能把 ANT 依赖版本升级传导到平台。

#### 独立解析信号夹具契约

reference fixture 使用确定性解析信号，不读取真实 EEG；采样数组由独立测试夹具生成器按下列公式计算，夹具不得导入平台生产 calculator 或 ANT `EEGProcessor`。所有输入为 `float64`、samples×channels、单位 V，时间为 `n / 100 Hz`；生成器用 `sin(2πfn/fs)`，相位统一 0，通道顺序固定为 `Fz,Pz,Oz,F3,F4`。基础稳定信号长度 40 s，恰好覆盖 2 s、4 s、30 s 窗和 Welch 整数频率格点。

| 通道 | 解析输入 | 主要用途/预期关系 |
|---|---|---|
| Fz | `20e-6 sin(2π·6t) + 10e-6 sin(2π·20t)` | theta/beta、BrainBeat Fz theta 与中线多频带。 |
| Pz | `20e-6 sin(2π·10t) + 5e-6 sin(2π·6t)` | IAPF 后部谱、BrainBeat Pz alpha、aperiodic pooled 输入。 |
| Oz | `15e-6 sin(2π·10t) + 8e-6 sin(2π·2t)` | IAPF 后部 ROI 与 pooled 谱。 |
| F3 | `10e-6 sin(2π·10t)` | FAA 配对基准。 |
| F4 | `20e-6 sin(2π·10t)` | FAA 解析值 `ln(4)`；对称预处理且无伪迹时作为方向与单位检查。 |

额外固定夹具变体：`artifact_pair` 在某一个完整 2 s 分段里给 F3 加入超过 150 µV 的正弦成分，验证 FAA 对 F3/F4 成对剔除且计数精确；`artifact_iapf` 在一个不重叠 2 s 检测窗内给后部输入加峰值超过 150 µV 的信号，验证坏窗掩码、clean ratio 和 0.75 门；`state_sequence` 将基础稳定信号重复成 45 s，提供 5 s 候选节拍、30 s 滚窗和目标前序状态重放；`frequency_transition` 将 alpha 正弦从 10 Hz 在明确的样本边界改为 12 Hz，验证锁定后 global 不更新、live/state 规则可逐窗核查；`invalid_inputs` 分别提供缺角色、NaN、Inf、零功率、截断频带与不足窗长，验证结构化不可用原因。所有变体的精确样本边界、生成参数和输入摘要哈希由 3.1 reference fixture manifest 输出并冻结；不把环境外生成的缓存数组作为唯一 source of truth。

解析夹具冻结了信号定义、可解析关系和一个独立 2 s Welch Fz 频率轴/PSD 数组 checksum。任务 3.1 已建立参考计算和身份检查；尚未生成所有 12 个算法的完整逐算法 expected arrays 与状态位置证据，这由 3.4/5.2 完成。若 specparam 拟合输出存在非确定性，须通过重复运行与环境指纹证据报告；不得用更宽容差掩盖。

迁移参考实现位于 `backend/app/tnk_reference/`，不会由 `builtin_modules.py` 注册，也不得被生产算法导入。机器可读环境、解析夹具生成公式、ANT 源文件 SHA-256、冻结 Welch 频率轴/PSD 数组 checksum 与容差见 `backend/tests/fixtures/tnk_reference_manifest.json`；生成和数值 checksum 测试只在冻结 ANT 环境执行，其他平台环境显示 skip，不把不同 SciPy/specparam 版本结果伪装成冻结基线。当前逐算法黄金数组和状态位置比较仍由 3.4/5.2 负责。

TNK 实现基座位于 `backend/app/algorithms/tnk/`，目前不进入生产 builtin 注册表。`contracts.py` 定义 samples/roles/PSD/单位/样本范围边界；`stateless.py` 提供 RBP、HAI、放松度、空间分布和 specparam 非周期指数；`stateful.py` 提供 IAPF、BrainBeat、BrainBeat Flat、疲劳和节律稳定性；`segment.py` 提供 segment-only FAA 与依赖证据驱动的综合指数。`artifacts.py` 只生成有界元数据和数组 checksum，实际 NPZ 持久化仍由现有 `ArtifactStore` 负责。模块测试通过后仍需 3.4 的逐项 parity/状态证据和第 4 章注册。

#### 来源分歧记录

ANT `AGENTS.md` §1.3 文字描述锁定需“连续 3 次”通过；`processor.py::try_lock_iapf` 的实际执行是每 5 s 检查，只追加合格候选，失败窗跳过且不清除已有候选，并以 3 个累计候选的中位数锁定。聚焦测试 `test_lock_skips_failed_windows_without_resetting` 明确断言该行为。TNK 重现以执行源码及测试为准，把该差异保留为迁移来源偏差；不复制已经过时的“连续”文字定义。

### 1.2 数值验收容差与精确比较规则

以下容差是“迁移实现对照独立参考”的最大接受误差，不改变算法频带、质量阈值或临床解释；所有量均在相同冻结环境、相同输入和相同预处理快照下比较。超出容差即不通过，不得通过放宽容差吸收定义差异。

| 输出量/算法 | 比较规则 | 理由/单位 |
|---|---|---|
| IAPF 频率轴、BrainBeat/Flat、Fatigue、FAA、RBP、HAI、aperiodic 的频率轴 | `array_equal` 精确相等 | 频率轴由同一采样率与 FFT/Welch 长度确定；Hz。 |
| IAPF PSD 数组 | `rtol=1e-6, atol=1e-18`；aperiodic/model log10-fit 数组 `rtol=1e-6, atol=1e-8` | PSD 单位 V²/Hz；log10-fit 数组按对数谱值比较，不能套用功率谱的绝对阈值。specparam offset/exponent/R²/MAE 是标量，按下方标量规则。 |
| IAPF、BrainBeat、BrainBeat Flat、Fatigue、HAI、FAA、RBP、放松度、空间分布、稳定性、综合指数、aperiodic exponent/offset 的有限浮点标量 | `rtol=1e-6, atol=1e-8`；IAPF 频率另限 `atol=1e-6 Hz` | 无量纲输出使用绝对误差下限 1e-8；积分功率使用 `rtol=1e-6, atol=1e-20 V²`；IAPF 频率单位 Hz。比较同时检查 `isfinite`，NaN/Inf 不参与近似相等。 |
| 伪迹窗掩码、可用通道集合、干净/总 epoch 计数、warm-up/lock/readiness 状态、quality/failure reason、实际样本边界、reset 后状态 | 精确相等 | 这些是离散契约，不能用数值容差接受状态或边界不同。 |
| EMA/历史状态及最终逐窗值 | 对每个状态转移逐窗应用对应标量/数组容差，并要求状态序列精确相等 | 不能只比较最终汇总值，避免不同更新顺序或陈旧值被掩盖。 |

FFT/Welch 参数按表内各算法逐项冻结：IAPF 是 MNE `Raw.compute_psd(method='welch')`、Hann、`n_fft=int(sfreq*4)`、`n_overlap=round(n_fft*0.5)`、1–30 Hz；前置 MNE notch/filter 使用冻结版本 1.12.1 的默认 FIR、zero-phase、Hamming window、firwin、auto filter length、reflect_limited padding。BrainBeat/Flat/Fatigue/实时 RBP 是 SciPy `signal.welch`、周期 Hann、默认 2 s `nperseg`、50% overlap、`nfft=nperseg`（输入短时按其实现缩短），`detrend='constant'`、`scaling='density'`、mean averaging。FAA 使用每个 2 s epoch 去均值、周期 Hann (`signal.get_window('hann', n)` 默认 `fftbins=True`)、NumPy `rfft/rfftfreq`、单边 density scaling、线性 PSD 平均，末尾不完整 epoch 丢弃。specparam 使用 `SpectralModel(aperiodic_mode='fixed', periodic_mode='gaussian', peak_width_limits=[1,8], max_n_peaks=4, peak_threshold=1.5)` 并拟合 3–30 Hz；频带边界、积分使用闭区间采样点上的梯形积分。IAPF 伪迹检测按不重叠 2 s 峰值窗、±150 µV；FAA 成对剔除超过同一阈值的 F3/F4 epoch。流式实时计算是 50 Hz notch + 4 阶 Butterworth 1–30 Hz 因果 SOS；FAA 时段报告用相同参数零相位 `sosfiltfilt`。IAPF 独立 MNE 零相位预处理与流式因果过滤是两条不同路径，禁止合并成一种预处理声明。

验证记录：在 ANT 项目锁定环境执行 10 个聚焦测试文件，`200 passed, 2 warnings`（MNE 对短数据滤波长度大于输入的警告，来自短缓冲拒绝测试）。该结果验证当前 ANT 源码测试，不等价于 TNK 迁移实现已达到上述数值参考；后者由任务 3/5 的独立参考与 parity 测试验证。容差策略冻结后仍须以迁移独立参考验证是否足够严格，若证据显示不满足则更新本账本并记录理由，不能静默扩大阈值。

### 冻结契约时必须保留的迁移约束

- ANT 实时过滤后的内部样本单位为 V；伪迹门配置为 µV 并乘 `1e-6`。但 ANT 原始采集映射/通道单位元数据需要在后续追溯，平台输入契约必须明确预转换约定。
- `ANT-Amplify/eeg-process-main/AGENTS.md` §1.3 描述锁定候选为“连续 3 次”，但当前 `try_lock_iapf` 对质量/拟合失败直接跳过、没有清空已有候选；当前执行源码因此是累计 3 个合格候选，并非连续成功。迁移以可执行源码和测试为准，须把这一文档差异作为明确来源偏差记录，不能照抄过时说明。
- `calculate_metrics` 的共同预处理为 50 Hz notch、4 阶 Butterworth 1–30 Hz 带通，以流式 SOS 跨 chunk；settle 前 3 s 不进计算。IAPF 每次又从原始缓存用 MNE 做整段 notch 与带通；这些是两条实际不同的处理路径，不能误写成共享一次滤波。
- 表中“默认值”是 ANT 源行为，不等于平台配置应允许用户改动。平台输入仍须以 V、samples×channels 和明确通道顺序提供；每模块的缺通道/质量/held 输出映射由平台结构化 Run 契约承接，不复刻 ANT 的零值或 UI 沿用值。
- 本节已通过锁定 ANT 环境中的聚焦测试核验并冻结参考环境、参数与比较容差；迁移侧是否满足容差仍是独立参考与 parity 阶段的验收项，不能提前声称通过。

### 2.1 确定性离线状态与上下文契约

本节把 ANT 的在线执行状态转换成离线 full/on-demand 可重放契约。它冻结计算次序、窗口定位和状态来源，不移植 ANT 的设备、线程或 GUI 生命周期。来源为 `processor.py` 的 `reset_buffers`、`should_update`、`try_lock_iapf`、`calculate_metrics` 及状态更新方法，并由相应聚焦测试核对。

#### 公共样本时间轴

- 每个 TNK Run 以样本索引 `reset_sample=0` 作为唯一默认状态重置边界，样本时间为 `sample_index / sfreq`。若调用者显式声明其他 segment/phase origin，该边界必须进入配置和 provenance；不得从请求到达时间、任务启动时间或当前窗口起点推断。
- 输入按文件时间升序提供 `(samples, channels)`。窗口使用 reset 锚定的半开样本区间 `[start_sample, end_sample)`；内部状态游标及窗口边界必须为整数样本。秒值只用于接口或展示映射。
- full、on-demand、combined 必须使用相同 reset/source、采样率、通道顺序与角色映射、滤波/参考配置、算法配置和窗口网格。on-demand 从同一 reset/segment origin 按样本顺序重放到目标 `end_sample`，只发布目标结果，不得仅计算目标窗口后伪造历史状态。
- canonical 更新位置由样本网格决定，不能由 reader chunk 分片决定。相同样本以不同 chunk 切割时，边界、转移顺序、状态序列和目标结果必须一致。
- 目标窗状态必须区分本窗新计算、warm-up、不可用等当前运行时状态。ANT 沿用上次有效值只作为来源事实；TNK 不得把旧值标成目标窗新计算结果。

#### 按模块冻结的历史依赖

| TNK 结果 | Reset / 输入上下文 | 状态转移与 cadence | 目标窗规则 |
|---|---|---|---|
| 放松度、空间分布 | Run reset；4 s 窗 | 每个 canonical 1 s tick 独立计算，无跨窗科学状态；先判断本窗输入与质量，再计算谱和指标 | 只需目标 4 s 窗。空间分布有效性须独立于分数保留。 |
| 节律稳定性 | Run/显式 phase reset；4 s 窗；每通道最近 10 个 alpha 比例 | 每 1 s tick 先给本窗实际可用通道追加比例，再基于更新后历史算 CV；每个参与通道至少 8 个历史项才 ready。缺失通道不更新。IAPF 锁定 transition 清空其历史 | 必须重放 reset 后的转移；不能只重放最近 8 窗，因为 10 项历史、缺失通道和锁定清空都会影响状态。 |
| 综合冥想指数 | 依赖稳定性、空间分布的同一 Run/phase origin 与窗口 | 等依赖状态更新后，按冻结权重聚合；不纳入 UI 最近 5 帧 median、惯性追随或随机启动值 | 目标值连同当窗权重、依赖项质量一起给出；聚合本身不增加历史状态。 |
| BrainBeat / BrainBeat Flat | Run reset；4 s 窗；两个结果各自独立 warm-up/EMA | 每 1 s tick 先检查 Fz/Pz 质量门再计算谱；有效窗的 log 比值前 3 个用于均值初始化，之后以 `alpha=0.15` 更新 log-EMA。拒绝窗或 Flat 拟合失败不推进该状态 | 从 reset 重放此前有效转移；拒绝窗不计 warm-up。目标处于 warm-up 时不得返回伪装成新算的数值。 |
| 疲劳指数 | Run reset；4 s 窗；Fz/Pz/Oz 各自独立状态 | 每 1 s tick 逐通道推进；仅该通道存在且 theta/beta 功率有效时，才累计 3 个 log 比值 warm-up 样本；初值为均值后 exp，随后 `alpha=0.15` log-EMA。缺失或无效通道不推进 | replay 至目标；每个通道有独立 ready 状态，不能共用 EMA 或要求所有通道同时 ready。 |
| TNK-IAPF | Run reset 或明确 IAPF segment origin；最近 30 s 输入 | 样本时钟每 5 s 尝试；每次以最近 30 s 估计并同步派生 HAI/aperiodic。合格候选更新 live；累计 3 个合格候选（失败尝试跳过且不清零）后 global 锁定为中位数，快照取最接近中位数的候选窗；锁定时清空稳定性历史。锁定后 global 固定，合格估计仍更新 live | 必须从声明 origin 按 5 s cadence 重放；provenance 区分 live、locked、global 来源窗和候选计数。不能用单个报告窗替代 rolling context。 |
| HAI、非周期指数 | 与 IAPF 相同的 30 s 谱输入、5 s 节拍和 reset 身份 | 谱可用时分别更新；HAI/拟合失败不覆盖 last-valid 状态。aperiodic 是直接更新，不做 EMA | 重放模块声明的谱转移；标明目标窗是否新算及失败/缺通道状态。可共享同源谱准备，但算法身份、质量和结果各自独立。 |
| 固定频段 RBP | Run reset；4 s 窗 | 每 1 s tick 仅在该能力激活时计算；无跨窗科学状态 | 只需目标窗；未启用、窗口不足或分母无效都返回结构化状态，不沿用上窗值。 |
| FAA | 显式事件 segment 起止；完整 segment | segment 完成后执行统一预处理、开头丢弃、对齐 epoch、成对质量筛选、线性 PSD 平均与 FAA；计算不依赖报告调用顺序 | 必须具备指定完整 segment 和相同事件边界。仅给游标窗时返回显式 unsupported/insufficient。 |
| BrainBeat report mode | 显式报告 segment；与 window mode 状态隔离 | segment 完成后单次计算原始谱 ratio，不读取或写入 window-mode warm-up/EMA | 请求完整 segment 报告；不得以逐窗 EMA 结果替代。 |

#### 固定时间轴与转移顺序

ANT 动态处理器使用 4 s 窗、3 s filter-settle gate 和 1 s 更新间隔。平台离线调度按样本数表达：动态指标首个 tick 不早于 `4·sfreq + 3·sfreq` 个样本，此后每 `round(1·sfreq)` 个样本一个 tick；4 s 窗起止固定在 reset 锚定网格。每次转移顺序固定为：推进预处理状态并判定 settle → 截取目标窗 → 生成谱 → 更新算法自有 warm-up/history/lock → 计算派生指标与组合结果 → 形成目标质量及 provenance。reader chunk 的切割不得改变任何 tick。

显式 Run reset 必须清空预处理状态、滚动缓存、cadence 计数、锁定候选、warm-up、EMA、稳定性历史和 last-valid 状态。phase/segment boundary 只重置模块契约明确列出的状态，不隐式影响其他算法。例如 ANT `start_meditation` 只重置 meditation warm-up 计数和稳定性历史，并不等价于 `reset_buffers`；TNK offline Run 仅在调用方提供该 phase 边界时才能应用相应局部 reset。

#### 对后续运行时实现的约束

- 每个有历史依赖的模块都须给出可复现的 `required_context_start_sample` 或 reset/segment identity；只提供 target bounds 的 stateful on-demand 输入不完整。
- 检查点/状态摘要只有在其身份覆盖 reset origin、输入源、预处理、采样率与通道顺序、算法/配置版本、phase/segment 边界及目标 end sample，并通过等价测试后，才可用于复用。
- replay 取消时不发布目标成功结果；运行时释放任务资源。上下文长度达到资源界限时，必须返回显式资源限制/unsupported，不得截断历史再输出似乎有效的数值。
- 本节冻结来源状态模型，不代表现有平台策略已支持该语义；上下文身份、复用、策略声明和取消测试分别由 2.2–2.4 实现与验收。
## ANT-Amplify raw-Fz additions (2026-10-28)

The following four internal children were added to the public `tnk_biofeedback` group. They are one source family but separate result identities.

| Internal ID | Source and preprocessing | Output/state | Artifact identity |
|---|---|---|---|
| `tnk_crimson_meditation` | Fz only; input is V/float64 and converted to µV; causal rational resampling to 250 Hz; 2–45 Hz band-pass then 58–62 Hz and 48–52 Hz band-stop; 1250 samples (5 s) | TFLite output index 2, clipped to 0–100, 20-point moving average; warm-up/quality/model failures are structured unavailable results | `cmsn_meditation.tflite`, SHA-256 `17cf291169f0e11f7868f7784332bbc4fbef483e363fd8bb0f67c3eedd82f644`; input `[1,1,1250,1]`, output `[1,3]`, float32 |
| `tnk_zenlite_calmness` | Fz only; V/µV conversion; causal rational resampling to 256 Hz; 2–45 Hz, 59–61 Hz and 49–51 Hz recursive sections; 1200 samples (4.6875 s) | Zenlite output index 0, clipped to 0–100, 10-point moving average; model failure does not disable the sibling spectral child | `zenlite-meditation.tflite`, SHA-256 `19fb19f948a2f86b59d01ce6ba2ca20a6034feb9941307184ee9ddbfe9517203`; input `[1,1,1200,1]`, output `[1,2]`, float32 |
| `tnk_zenlite_awareness` | Same source and preprocessing as calmness | Zenlite output index 1, clipped to 0–100, 10-point moving average | Same Zenlite asset and shape contract |
| `tnk_zenlite_spectral_relaxation` | Same 256 Hz/1200-sample filtered Fz window; SciPy FFTPACK real layout is paired as adjacent magnitudes; bands are delta 0.5–4, theta 4–8, alpha 8–12, low beta 12–22, high beta 22–32, gamma 32–56 Hz | `alpha/(beta_low+beta_high)`, mapped by `(ratio-0.1)/0.8*100`, clipped to 0–100, 10-point moving average; this is an amplitude proxy, not Welch PSD | No model dependency; the FFT and band boundaries are part of the child implementation |

The raw source boundary is `load_time_series_source()` in `backend/app/services/algorithm_recording_context.py`. It preserves recording/montage identity, exact half-open sample bounds, channel order, V/float64 values, and rejects non-finite samples. The legacy children replay from the declared run origin for deterministic offline results; this is not a claim of live-stream equivalence. The optional `ai_edge_litert` dependency is intentionally lazy. Missing runtime/model validation returns `TNK_MODEL_UNAVAILABLE`; no zero or held value is substituted.
