# 九个官方算法阶段性评级与问题清单

日期：2026-10-20<br>
适用基线：docs/validation/2026-10-20_algorithm-audit-baseline.json 中的九个离线模块身份。<br>
证据范围：阶段 0–4 静态、自动化边界、合成独立参考和动态稳健性；阶段 5 当前版本授权真实记录核验尚未完成。

## 评级规则

沿用审计计划的评级定义。A 要求真实目标数据独立验证；B 表示工程和合成数值证据扎实、真实数据证据有限；C 表示关键边界、独立参考或 provenance 缺失；D 仅用于可重复证明违反已批准契约的情况。评级只表示工程/数值证据，不表示临床有效性。

状态：通过 / 有条件 / 未验证 / 失败。阶段 5 未完成限制所有算法不能获 A。当前没有足够证据将任何风险判为 D。

## 九算法评级

| 算法（基线版本） | 工程实现 | 独立复算 | 边界覆盖 | 来源追溯 | 暂定评级 | 证据与限制 |
|---|---|---|---|---|---|---|
| PSD (offline-spectral-v3) | 有条件 | 有条件 | 有条件 | 有条件 | B | Welch/STFT 数值原语、动态 full/on-demand 逐窗、artifact identity 有证据；阶段 3 PSD 独立窗测试通过。完整当前版本原始文件滤波链路及授权真实数据未复核。 |
| STFT (spectrogram-v2) | 有条件 | 有条件 | 有条件 | 有条件 | B | FFT 矩阵、时间/频率轴、坏帧和单位有独立合成及边界测试；真实记录与当前桌面展示核对缺失。 |
| Peak Frequency (official-peak-frequency-v1) | 有条件 | 有条件 | 有条件 | 有条件 | B | 离散峰选择、边界/tie 与数值参考通过；非有限多通道切片会产生空切片 RuntimeWarning，已测为不可用但实现可进一步清理。 |
| IAPF (official-iapf-v2) | 有条件 | 有条件 | 有条件 | 有条件 | C | 合成谱独立拟合比较及退化边界存在；非正/非有限 PSD 会产生 warning，且 estimate_iapf 可能以 value=None, gate_failed=None 返回，纯 1/f 输入的诊断 peak 字段也需专门复核。属于待确认的模块边界/失败语义风险，不是已确认用户路径缺陷。 |
| Band Ratio (official-band-ratio-v1) | 有条件 | 有条件 | 有条件 | 有条件 | B | 独立边界插值积分、分母、范围失败和 dynamic 状态有覆盖；真实原始 EEG 到指标端到端缺失。 |
| Theta/Beta (official-theta-beta-v2) | 有条件 | 有条件 | 有条件 | 有条件 | C | 组合中间量有独立谱参考，runner 越界返回结构化失败；IAPF 靠近边界的科学定义尚未确认，不能把低层 helper 的 ValueError 描述为未处理用户路径错误。 |
| RBP (official-rbp-v2) | 有条件 | 有条件 | 有条件 | 有条件 | B | 五频段积分/归一化、Gamma 覆盖和历史版本隔离有证据；50 Hz notch 对 Gamma 的真实记录影响尚未在本 change 重验。 |
| FAA (official-faa-v2) | 有条件 | 有条件 | 有条件 | 有条件 | B | paired epoch FFT、F3/F4 顺序、质量配对和符号对照有独立合成证据；跨采样率及当前授权真实记录覆盖不足。 |
| 离线 Brainbeat (official-brainbeat-v1) | 有条件 | 有条件 | 有条件 | 有条件 | C | Fz/Pz 逐窗独立 IAPF/指标与执行顺序有合成对照；端到端原始输入和真实数据 provenance 未核验。不得用离线证据支持实时 IAPF-lock/EMA BrainBeat。 |

“工程实现/边界/来源追溯”标为有条件，是因为当前证据来自静态审阅和自动化夹具，不等同于当前版本目标数据集的全链路现场核验。独立复算“有条件”特指合成输入通过，不表示真实记录复算通过。

## 按优先级排列的 backlog

| ID / 优先级 | 项目与证据 | 影响 | 建议处理及验收 | 回滚/兼容策略 |
|---|---|---|---|---|
| A-01 / P0 | 当前版本授权记录的后端独立复算已完成（9 算法、32 组配置、407456 结果点、0 失败）；当前 API/artifact checksum 与桌面展示对应仍未逐项核对，且未持久化正式 ValidationRun/数组 artifact。 | 后端对照证据不能单独证明当前 API、artifact 与 WPF 展示链路一致。 | 补做当前 API/artifact identity、checksum、状态和实际范围核对，并核验桌面结果绑定；正式保存摘要和大数组 artifact 或记录具体障碍。 | 只新增审计 ValidationRun/artifact，不覆盖源文件和历史 Run；仅删除本次生成的派生验证工件。 |
| A-02 / P1 | IAPF 非正/非有限 PSD 直接 log10/polyfit 发出 RuntimeWarning；低层 helper 仅 gate_failed=None，外层 runner 返回通用 IAPF_UNAVAILABLE，静态审阅与边界测试可复现 | 有结构化失败码，但无法区分无有效 peak/COG 与输入域无效； warning 暴露退化数值路径 | 先由科学/契约负责人决定该 helper 的输入域；若批准细分失败原因，在独立 OpenSpec change 定义 code/reason 与诊断字段语义，并增加 helper + runner 测试。验收要求无 warning、结构化原因、无非有限 evidence。 | 新 scientific version/implementation identity 或明确兼容策略；历史 Run/artifact 只读，并验证旧结果仍可读取。 |
| A-03 / 已撤销 | “有限纯 1/f 输入的 peak_hz 会为 NaN”假设已用 NumPy 2.3.4 和当前生产函数复查；平坦 PSD 与 1/f PSD 都给出有限 peak_hz=7 Hz、COG 约 10 Hz，且 source=cog。 | 没有复现原假设，不登记为缺陷或待修复项。 | 不创建修复任务；若其他频率网格或输入今后出现非有限诊断字段，再补充最小复现和证据。 | 无生产改动、无迁移；保留本次复核记录以更正此前审计文档中的错误推断。 |
| A-04 / P1 | Theta/Beta 低 IAPF 形成空/倒置 theta 带；runner 当前返回 BAND_RANGE_INVALID，科学定义是否应改边界未确认 | 边界附近窗口可能 unavailable，且用户解释需保持准确 | 领域负责人批准空频带策略（unavailable 或重新定义频段）；保留当前 4/6/30 Hz 附近测试，增加状态、failure 和 evidence 验收。公式变更另开 contract change。 | 不在审计 change 中改公式；新版本与旧定义并存，历史 Runs 使用其原版本。 |
| A-05 / P2 | RBP 50 Hz notch 与 30–50 Hz Gamma 比值的真实输入影响，本 change 只有合成带功率对照 | 合成谱不能代表陷波器对真实 Gamma 能量/谱形的影响 | 授权真实记录阶段同时保存 notch 配置身份，比较滤波前后Gamma参考量与 Run evidence；不改变已声明 notch，仅评估其实际解释影响。 | 仅报告观测；若需改变 notch 或 Gamma 定义，另开 scientific-contract change 与新版本。 |
| A-06 / P2 | 旧实时 BrainBeat IAPF-lock/EMA 不属于离线 brainbeat 证据范围 | 用户可能误把两套状态/窗口语义当成同一算法 | 单独为 realtime/shadow 状态建立 change：冻结状态机、候选锁定、EMA、reset、gap 和 provenance，再做序列故障注入。 | 不影响离线 official-brainbeat-v1 和历史 Run；实时结果采用独立 identity。 |

当前没有经证实的契约违例需要在本 change 中创建修复 OpenSpec change。A-02、A-04 至 A-06 先作为风险/科学决策 backlog；A-03 已撤销。确认契约违例或批准公式变更后再开独立 change。没有创建空的修复 change。

## 结论边界

- 当前合成独立参考通过和动态/故障注入测试，只支持限定环境与样例上的工程/数值一致性。
- 本 change 的授权真实记录验证尚未完成；没有临床样本统计设计、对照标签、统计功效或伦理研究，因此不作临床/诊断有效性结论。
- 阶段性 B/C 评级不得对外简写成“算法已验证”而省略数据范围、当前版本和未完成的真实记录验证。
