# 频谱与离线指标

## 当前执行与版本

当前九算法的身份、科学版本、参数、输出和策略能力由 `backend/app/algorithms/<id>/manifest.py` 声明。共享运行时协调 full、on-demand 和 combined，算法模块负责科学计算；各方向的复用支持以 manifest 和验证矩阵为准，不能从 dynamic 模式推断。

共享离线频谱基线是 `offline-spectral-v3`，STFT 和各指标有各自的 scientific version。仅维护显式 current version，不提供退役计算器、旧实时指标或历史结果解释适配。新增算法流程见 [统一管理基线](official-algorithm-management.md)。

## 频谱与单位

离线预处理与频谱由 `backend/app/scientific/primitives/spectral.py` 提供。内部 EEG 为 V/float64，PSD 为 `V^2/Hz`；输出展示单位必须显式转换。通道顺序和实际样本范围随结果保留。

默认频谱契约使用 4 阶 Butterworth SOS 零相位滤波、4 s Hann Welch 片段和 50% 重叠；频率范围、陷波和算法特定配置以冻结的执行快照为准。只平均通过质量检查的片段；非有限值、幅度异常、平线、削顶或缺样保留明确证据。质量门失败返回不可用，不伪造零值。

通用 band-power 使用精确边界插值和梯形积分。Brainbeat 的当前公式保留其独立的 inclusive-bin 积分及 epsilon；不能在管理重构中替换为另一积分方式。

## 当前指标

- Peak Frequency：在声明频段内选择峰值频率，单位 Hz。
- IAPF：在 3–30 Hz 的对数频谱拟合中排除 7–13 Hz，依据残差峰值与 COG 规则返回个体 Alpha 频率；质量和失败原因一并保存。
- Band Ratio：按配置的分子、分母频段功率计算比值，实际积分范围与中间功率写入 evidence。
- Theta/Beta：当前为一个明确原始通道的 IAPF 相对频段比值，`theta=[max(4,IAPF-6),IAPF-2]`、`beta=[IAPF+2,30]`，不要求 Fz/Pz/Oz 三通道组合。
- RBP：当前输出 Delta、Theta、Alpha、Beta、Gamma 五频段功率及相对比例；边界和总分母由 RBP 模块声明，不使用旧实时四频段实现。
- FAA：F3/F4 明确选择来源，使用请求绝对范围内的成对 2 s epoch、50% overlap 和质量门，默认至少十个 clean paired epochs；计算 `ln(P_F4_alpha)-ln(P_F3_alpha)`。不再保留历史起始丢弃或最长时长常量。
- 离线 Brainbeat：同一窗内独立计算 Fz/Pz 频谱及 IAPF，得到 `relative_theta(Fz)/relative_alpha(Pz)`，不跨窗进行 IAPF 锁定、warm-up 累积或 EMA。

## 时间、质量与追溯

静态请求范围、动态计算窗和显示历史长度分别管理。范围使用录制相对样本坐标，秒数由采样率推导；预热、实际范围和拒绝状态必须显式返回。各算法的最小长度和质量规则以自身参数与执行快照为准。

WPF 仅呈现后端结果。完整 STFT 窗口缺少当前预处理范围或内部时间轴证据时，返回 `FULL_WINDOW_CONTRACT_UNAVAILABLE` 并要求重新计算，不推断 provenance。原始记录、Run 和 artifact 字节不会因此被删除或改写。

数值验证使用独立参考及受控 JSON/NPZ 基线；工程验证通过不代表临床有效性或医学诊断能力。
