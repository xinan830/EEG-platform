# 九算法契约与实现审阅（阶段 1）

审阅日期：2026-10-20<br>
依据：各算法 `manifest.py`、`config.py`、`runner.py`、`compute.py/official.py`，`openspec/specs/analysis/*` 与现有后端测试。<br>
范围：静态契约和实现审阅；不替代真实记录独立复算，不作临床结论。

## 共性结果

1. 九个模块都由 `backend/app/algorithms/builtin_modules.py` 的单一组合入口注册；manifest 提供稳定 ID、科学版本、实现身份、模式、输出 schema 和可运行状态。
2. 动态算法使用 runtime 的窗口规划和结构化结果；结果保留实际范围、质量、失败和 evidence。当前测试已覆盖一部分 warm-up、失败窗口和历史版本语义。
3. 原始信号在后端按 V/float64 进入科学计算；公开输出显式区分 Hz、V²/Hz、ratio、dimensionless 和 dB。前端没有承担 EEG 公式计算。
4. PSD/STFT/RBP 等频谱类算法与 FAA/Brainbeat 的双来源复合算法在科学上不应强行共用公式；可统一的是身份、时间、单位、质量和 provenance 检查。

## 逐算法审阅结论

| 算法 | 已核实符合项 | 风险/待验证项 | 阶段 1 状态 |
|---|---|---|---|
| PSD | Welch/滤波/频率轴和动态窗口由模块与频谱原语承载；质量门失败返回结构化状态；输出 schema 明确 | 真实样本逐点独立 Welch；不同采样率和 Nyquist 边界；Full artifact 与显示链路一致性 | 有条件通过 |
| STFT | 4 s 窗、1 s 步进、窗口中心、线性/对数功率和坏帧语义有代码及独立 FFT 测试 | 窄频段、短范围、每帧矩阵维度和 PSD 对齐比较需扩大；真实记录复算未完成 | 有条件通过 |
| Peak Frequency | 频段内离散 bin 最大值、包含边界、并列取低频、无 bin 失败均有实现/测试 | 非有限频率轴、多通道顺序和原始输入到最终峰值的完整独立复算 | 有条件通过 |
| IAPF | 3–30 Hz 拟合、排除 7–13 Hz、prominence 选择 peak/COG、失败不返回数值；有合成参考 | 负/零/非有限 PSD 会产生 NumPy warning 和 `nan` model_error 后以 `no_peak_no_cog` 结束；生产频谱通常会把值限制为正，但该函数缺少显式输入防护。需要决定这是模块边界约束还是函数契约缺口 | 风险待确认 |
| Band Ratio | 使用权威边界插值梯形积分；检查分母正数、非有限和频段覆盖；独立积分测试存在 | 非有限 PSD、重叠频段、相接边界和真实原始输入复算需补齐 | 有条件通过 |
| Theta/Beta | 依赖当前窗 IAPF；按 IAPF 生成个体化 Theta/Beta 频段；失败返回 null/结构化失败；有组合参考 | IAPF 靠近 4/6/30 Hz 等边界时的空频段、超轴和失败质量语义需补齐；真实复算未完成 | 风险待确认 |
| RBP | 五频段 1–50 Hz、边界积分、总和归一化、Gamma/50 Hz 约束和历史版本契约明确；独立常密度参考存在 | notch 对 Gamma 的实际影响和不同采样率端到端复算仍需证据；确认旧四频段结果不被当前 UI 混读 | 有条件通过 |
| FAA | 显式 F3/F4、连续预处理后 paired 2 s Hann/50% epoch、`ln(P_F4)-ln(P_F3)`、质量门和短数据失败明确；独立 FFT 参考存在 | 不同采样率、坏 epoch 成对剔除、左右交换符号关系和真实记录复算待完成 | 有条件通过 |
| 离线 Brainbeat | 显式不同 Fz/Pz；每窗口独立 IAPF；Theta(Fz)/Alpha(Pz)；无 EMA 跨窗；失败返回 null；配置和动态测试存在 | 独立端到端参考覆盖不足；必须继续与实时状态性 BrainBeat 分开；真实记录复算未完成 | 有条件通过 |

## 已确认的风险，不等同于已确认缺陷

IAPF 的直接边界探测显示，传入负、零或非有限 PSD 时会出现 NumPy runtime warning，并返回带 `nan` 模型误差的不可用结果。当前生产频谱原语会将通过质量门的 PSD 下限限制在正值，因此这更准确地记录为“模块输入契约是否应显式防护”的风险，而不是已经证明的用户路径 bug。阶段 3 必须补测试并决定：在 `estimate_iapf` 内拒绝并结构化返回，还是由上游强制保证正值并把约束写入契约。

## 当前评级前提

阶段 1 不给 A/B/C/D 最终评级。现阶段最多说明：实现结构和工程测试基础总体良好；真实数据独立复算、部分边界和完整 provenance 证据尚未齐全。最终评级必须等阶段 2–5 的证据完成后再给出。

## 阶段 2 边界与回归验证

验证日期：2026-10-20。新增边界审计测试分为两个文件，避免测试文件混合职责并控制文件规模：

- backend/tests/test_official_algorithm_boundary_conditions.py
- backend/tests/test_official_algorithm_extended_audit.py

覆盖内容包括：IAPF 拟合点不足、平坦谱、非正/非有限 PSD、Alpha 无 bin、Theta/Beta 个体化频段越界；PSD/Welch 频率轴、单位、矩阵维度、质量门和缺口拒绝；STFT 线性功率/dB 单位、坏帧 NaN、窗口中心和动态 warm-up；Peak Frequency 包含边界和并列峰；Band Ratio 边界插值积分及无效分母；RBP Gamma 覆盖、五频段版本身份；FAA F3/F4 角色、成对 epoch 质量和参数顺序；离线 Brainbeat Fz/Pz 角色、失败 null 与跨窗口独立性。

结果：

- 第 3 章相关后端测试：137 passed；
- 全量 backend/tests：816 passed；
- 发现并保留 5 条预期 RuntimeWarning：退化 PSD 输入触发 log10/nanmean 警告；测试验证其结果仍不可用或保持结构化失败；
- 未进行 GUI 人工验收，也未把工程测试通过解释为临床有效性；
- IAPF 对非正/非有限 PSD 的 warning 和 Theta/Beta 边界定义仍属于科学契约风险，未在本审计 change 中改公式。
