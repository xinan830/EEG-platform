# 频谱与离线指标

## 当前执行与版本

当前 10 个独立算法的身份、科学版本、参数、输出和策略能力由 `backend/app/algorithms/<id>/manifest.py` 声明；TNK 指标组由组 manifest 和运行时适配器提供一个目录项。共享运行时协调 full、on-demand 和 combined，算法模块负责科学计算；各方向的复用支持以 manifest 和验证矩阵为准，不能从 dynamic 模式推断。

共享离线频谱基线当前为 `offline-spectral-v4`：分析预处理默认 1–50 Hz、50 Hz/Q=30 陷波、零相位；可用算法配置显式关闭陷波。Review 显示滤波不进入分析输入或身份。v3 保持冻结，仅用于解释历史结果。算法可因科学定义声明更窄或更宽的滤波范围，但必须记录有效范围；共享基线不统一算法窗口、Welch 参数、输出频段、频带和质量规则。新增算法流程见 [统一管理基线](official-algorithm-management.md)。

| 算法 | 实际预处理 | 估计窗口 / 输出 | 算法自有目标与证据 |
| --- | --- | --- | --- |
| PSD | 1–50 Hz；默认 50 Hz/Q=30，可关或选 60 Hz | 4 s Hann Welch，50% overlap、2 s step；输出轴由配置决定 | Delta/Theta/Alpha/Beta/Gamma 按各自边界积分；Review 地形图从 PSD 密度积分选中频带并输出 `uV^2`；Run 保存输出范围、预处理与质量证据 |
| Peak Frequency、Band Ratio、Theta/Beta、IAPF、Brainbeat、TNK | 1–50 Hz；默认 50 Hz/Q=30，可显式关闭或选 60 Hz | 窗口由算法策略声明；共享 PSD 消费者使用 4 s Hann Welch、50% overlap | 搜索频段、分子/分母、个体化频带、IAPF 拟合范围及子模块频段均保留算法定义并写入快照/结果 evidence |
| RBP | 1–50 Hz；默认 50 Hz/Q=30，可关或选 60 Hz | 4 s Hann Welch，50% overlap、2 s step；使用完整轴 | Delta、Theta、Alpha、Beta、Gamma；每通道质量、相对比例与绝对功率分别记录 |
| STFT | 1–50 Hz；默认 50 Hz/Q=30，可显式关闭或选 60 Hz | 4 s 内窗、1 s step；输出频率轴由配置决定，预处理使用有限上下文 | 目标/读取样本范围、零相位和逐窗质量随矩阵返回 |
| FAA | 配置的预处理范围，默认 1–50 Hz；默认 50 Hz/Q=30，可关或选 60 Hz | 请求范围内成对 2 s epoch，50% overlap；不使用 Welch | F3/F4 配对质量与 Alpha 功率 evidence 保留 |

## 频谱与单位

离线预处理与频谱由 `backend/app/scientific/primitives/spectral.py` 提供。内部 EEG 为 V/float64，PSD 为 `V^2/Hz`；输出展示单位必须显式转换。通道顺序和实际样本范围随结果保留。

默认频谱契约使用 4 阶 Butterworth SOS 零相位滤波与独立的 50 Hz/Q=30 零相位陷波。PSD 的 Welch 段长、重叠和算法特定频率输出由各模块声明；当前 PSD 使用 4 s Hann 段和 50% 重叠。只平均通过质量检查的片段；非有限值、幅度异常、平线、削顶或缺样保留明确证据。质量门失败返回不可用，不伪造零值。

所有调用共享 `scientific/primitives/spectral.py::band_power` 的频带积分：将边界 PSD 线性插值到精确频率，再做梯形积分。BrainBeat 现有两个当前身份：经典风格 `brainbeat` 使用 Fz 4–8 Hz 与 Pz 8–12 Hz 绝对功率比；`brainbeat_individualized` 保留 IAPF 相对频带和各通道 1–30 Hz 分母。两者默认动态窗均为 20 s、每 1 s 更新，支持完整 10/20/30 s 窗口且不输出部分预热值。主文献页面当前可确认算法名称与研究背景，但可访问摘要没有足够方法细节证明经典身份是 Holm 等人完整方法的精确复现；该频带/绝对功率定义仍需对照论文全文验证。历史 Run 不重算或改写。

## 当前指标

- Peak Frequency：在声明频段内选择峰值频率，单位 Hz。
- IAPF：在 3–30 Hz 的对数频谱拟合中排除 7–13 Hz，依据残差峰值与 COG 规则返回个体 Alpha 频率；质量和失败原因一并保存。
- Band Ratio：按配置的分子、分母频段功率计算比值，实际积分范围与中间功率写入 evidence。
- Theta/Beta：当前为一个明确原始通道的 IAPF 相对频段比值，`theta=[max(4,IAPF-6),IAPF-2]`、`beta=[IAPF+2,30]`，不要求 Fz/Pz/Oz 三通道组合。
- 独立 RBP（`official-rbp-v4`）：结构化结果输出 Delta、Theta、Alpha、Beta、Gamma 五频段相对比例（`ratio`）及绝对功率（`uV^2`）；绝对功率与比例来自同一 PSD 积分，地形图可切换显示类型而不重跑算法。频段边界和相对功率总分母由 RBP 模块声明。质量按通道独立评估，幅值超限、疑似削顶和低幅值等保留有限数据的计算值并标注警告，不再受 75% 干净片段比例限制。缺失/非有限样本、无效五频段总功率仍令该通道不可用；不插值、不补零。保留伪迹会改变数值含义，不能解释成经过伪迹剔除的干净脑电。TNK 和其他指标的质量策略不随之改变。
- FAA：F3/F4 明确选择来源，使用请求绝对范围内的成对 2 s epoch、50% overlap 和质量门，默认至少十个 clean paired epochs；计算 `ln(P_F4_alpha)-ln(P_F3_alpha)`。不再保留历史起始丢弃或最长时长常量。
- 经典风格 BrainBeat（`official-brainbeat-v6-classic-absolute`）：共享 4 s Hann Welch / 50% overlap；指标为 Fz 4–8 Hz 绝对功率除以 Pz 8–12 Hz 绝对功率，V²/V² 为无量纲 ratio，不依赖 IAPF。研究背景见 [Holm et al. (2009)](https://pmc.ncbi.nlm.nih.gov/articles/PMC5823228/)；当前工程尚未依据论文全文逐项核实频带、功率定义和频谱估计细节，因此不宣称精确复现。
- 个体化 BrainBeat（`official-brainbeat-individualized-v1-relative`）：每窗独立计算 Fz/Pz 频谱及 IAPF；指标为 `relative_theta(Fz)/relative_alpha(Pz)`，各自分母为 1–30 Hz，不跨窗进行 IAPF 锁定、warm-up 累积或 EMA。

## 时间、质量与追溯

静态请求范围、动态计算窗和显示历史长度分别管理。范围使用录制相对样本坐标，秒数由采样率推导；预热、实际范围和拒绝状态必须显式返回。各算法的最小长度和质量规则以自身参数与执行快照为准。

WPF 仅呈现后端结果。完整 STFT 窗口缺少当前预处理范围或内部时间轴证据时，返回 `FULL_WINDOW_CONTRACT_UNAVAILABLE` 并要求重新计算，不推断 provenance。原始记录、Run 和 artifact 字节不会因此被删除或改写。

数值验证使用独立参考及受控 JSON/NPZ 基线；工程验证通过不代表临床有效性或医学诊断能力。
