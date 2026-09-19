# 离线频谱分析 v2

## 根因

- FastAPI 离线分析实际调用 `processing/offline_analysis.py`，与旧实时 `EEGProcessor` 是两条不同链路。
- 旧离线链没有显式整段预滤波，伪迹剔除后会拼接不连续好样本再做 Welch。
- 离线 `brainbeat` 曾使用 `log10(beta/theta)`，与实时同名指标的 `theta_Fz/alpha_Pz` 不一致。
- F3/F4 映射存在，但离线结果没有计算 FAA。

## 新契约

- 版本：`offline-spectral-v2`。
- 输入：原始记录电压，单位 V，不做软件重参考。
- 预处理：整段 4 阶原型 Butterworth、1–30 Hz、SOS、`sosfiltfilt` 零相位。
- PSD：4 秒 Hann epoch、50% epoch 重叠；成对剔除任一映射通道超过 ±150 uV 的 epoch；在线性功率域平均。
- IAPF：30 秒窗口、5 秒步长、7–13 Hz 搜索，连续 3 个有效候选中位数锁定。
- IAPF 的 1/f 普通最小二乘拟合排除 7–13 Hz，峰值从线性残差谱选择；指标采用离线全局锁定值，否则使用最后候选或 10 Hz 默认值并标明来源。
- Brainbeat：Fz theta 相对功率 / Pz alpha 相对功率。
- 空间分布：Fz alpha 相对功率 / Pz+Oz alpha 相对功率；后部 alpha 低于 0.02 时返回不可用。
- 疲劳：逐通道 theta/beta；节律稳定性使用最近 8 个有效点的 alpha 相对功率 CV。
- FAA：共享领域函数 `ln(P_F4)-ln(P_F3)`；离线范围为全记录丢弃开头 12 秒，非事件区间。

## 可追溯性

- 结果包含算法契约、采样率、样本数、单位、映射通道和参考语义。
- 低质量指标返回 `null` 和 `gate_failed`，不再返回伪造的 0。
- 历史结果读取时标记 `legacy-unversioned`，不会冒充 v2。
- 旧实时链明确标记 `realtime-eegprocessor-v1`，不能与离线 v2 逐点等同。

## 验证与限制

- 独立 SciPy 参考验证零相位 SOS；人工 PSD 验证 Brainbeat 与空间分布公式；合成信号验证 IAPF 和 FAA。
- 真实 BDF 烟雾测试可以稳定完成，但其输出不是临床真值或医学有效性证明。
- 当前 IAPF 的非周期拟合是 log-log 普通最小二乘，不等同于实时链的 specparam；临床阈值与解释仍需数据集和专家验证。
- “综合指数”、Brainbeat 和疲劳比值均为探索性产品指标，不是临床诊断结论。
