# 旧实时处理器指标

本文件记录 `EEGProcessor` 的流式观察指标。它们服务于连续回放/旧实时接口，与 `offline-spectral-v3` 是不同版本，不能把同名字段当成数值等价。

## 共同输入

处理器对输入 chunk 使用有状态因果 SOS 陷波和带通，保留跨 chunk 的滤波状态；指标使用最近 4 s buffer，逐帧去均值。默认实时带通为 1–30 Hz，实时 Welch 常用 2 s Hann segment、50% overlap。滤波建立期约 3 s 的样本不进入稳定指标。

## RBP

实时 RBP 对每个可用通道积分 Delta 1–4、Theta 4–8、Alpha 8–13、Beta 13–30 Hz，然后除以四段功率之和。旧实现使用离散频点的 inclusive mask，和 v3 的“插值边界 + 半开区间”并不完全相同。无有效分母时通道进入 `invalid_channels`，不返回伪造比例。

## BrainBeat 与疲劳

- BrainBeat：`relative_theta(Fz) / relative_alpha(Pz)`。相对功率分母分别是同通道 1–30 Hz 总功率；结果在 log 域经过 warm-up 和 EMA，再指数还原。伪迹或通道缺失时保留上一有效值。
- `brainbeat_flat`：先对 Fz/Pz 各自 PSD 拟合并扣除 1/f 残差，再按同样的相对 theta/alpha 结构计算；这是观察字段，不替代原始 BrainBeat。
- Fatigue：同通道 `theta / beta`，默认 Fz/Pz/Oz，使用 IAPF 相对边界 `theta=[max(4,IAPF-6), IAPF-2]`、`beta=[IAPF+2,30]`；同样在 log 域平滑。

## IAPF、1/f 与 HAI

处理器每 5 s 在最近 30 s 窗尝试 IAPF。锁定前合格候选累计到 K 次后取中位数冻结 `iapf_global`；锁定后只更新 `iapf_live`。未通过峰值质量门时保留上一有效值。

同一 IAPF 频谱还派生：

- 1/f slope：对数功率与对数频率拟合的非周期指数，逐通道及 Pz+Oz pooled 版本。
- HAI：`log10(P[13,25] / P[1,8])`，使用原始 PSD，不扣 1/f，仅用于趋势展示。

## 映射分数与稳定性

放松度、空间分布和节律稳定性通过固定经验映射为 0–100：放松度和空间分布使用 logistic，稳定性使用 `1/(1+CV)` 后按 0.40–0.90 做 clip。稳定性历史默认 8 点，Fz/Pz/Oz 权重为 0.2/0.4/0.4；历史不足时返回中性值，不返回虚假的满分。

这些映射是产品观察分数，不是临床量表。修改经验中心、斜率、权重或 EMA 参数必须更新实时算法版本、测试和变更文档。
