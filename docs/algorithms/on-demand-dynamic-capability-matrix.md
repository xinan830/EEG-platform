# 按需动态算法能力矩阵

更新时间：2026-10-14

这份矩阵区分两个容易混淆的概念：manifest 的 `supported_modes` 表示算法可以通过旧
的完整 Run 生成动态序列；`DynamicWindowCapability` 则表示算法能够在后端根据一个
游标规划并执行单个窗口，且有独立的数值证据。只有后者允许接入
`/api/analysis-windows/ensure`。

| 算法 | 旧完整动态 Run | 按需单窗能力 | 当前处理 | 窗口/尾窗规则 | 预处理与质量边界 |
| --- | --- | --- | --- | --- | --- |
| PSD | 是 | 是（生产开关关闭） | capability 与真实记录 114 窗口等价、小时资源及全量自动化门通过；等待 P3 GUI | `psd-dynamic-window-v1`；后端规划已到达的最新合规窗口；不足返回 `insufficient_data` | 沿用整段连续 zero-phase source 语义；每窗复用既有 PSD 质量门；不允许局部滤波；gap NaN 传播限制保留 |
| STFT | 是 | 是（生产开关关闭） | capability 与完整动态 Run 的时频轴、矩阵、质量和 provenance 自动化对照通过；等待阶段 5 GUI | `stft-dynamic-window-v1`；4 秒内部段、1 秒 step；首个最小窗只发布真实存在的时频行，禁止填充 NaN 到按需 READY | 复用连续预处理后的 spectrogram loader；ready 结果声明绝对 recording 时频中心轴、`V^2/Hz` 与 `dB re 1 uV^2/Hz`；质量拒绝不发布矩阵 |
| RBP | 是 | 是（生产开关关闭） | capability 与真实格式 API/WPF 投影对照通过；等待后续 GUI 总验收 | `rbp-dynamic-window-v1`；五频段按后端窗口规划；尾窗/质量按旧 runner | 固定 1–50 Hz、50 Hz 陷波、连续 source 后取窗；不借用 PSD 可配置滤波；缺口/质量拒绝保留旧失败语义 |
| 频段功率比 | 是 | 是（生产开关关闭） | scalar capability 与真实格式 API/WPF 投影对照通过；等待后续 GUI 总验收 | `scalar-dynamic-window-v1`；配置分子/分母频段；窗口长度决定 Partial | 连续 source 后取窗；正分母门、频段 evidence、单位和失败原因与完整 Run 对照 |
| 频段峰频率 | 是 | 是（生产开关关闭） | scalar capability 与真实格式 API/WPF 投影对照通过；等待后续 GUI 总验收 | `scalar-dynamic-window-v1`；按配置频段和既有频率网格选峰 | 连续 source 后取窗；峰频率单位/网格/质量/evidence 与完整 Run 对照 |
| IAPF | 是 | 是（生产开关关闭） | scalar capability 与真实格式 API/WPF 投影对照通过；等待后续 GUI 总验收 | `scalar-dynamic-window-v1`；沿用峰/COG 估计和动态窗口规则 | 连续 source 后取窗；峰/COG、残差和不可用原因由模块 evidence 返回 |
| FAA | 是 | 是（生产开关关闭） | capability 与完整动态 Run/独立参考对照通过；等待阶段 3 GUI 验收 | `faa-dynamic-window-v1`；默认 20 秒，最小 11 秒，禁止 warmup；2 秒 epoch、1 秒 step，尾窗由后端规划 | 成对 F3/F4；连续预处理后再分 epoch；150 µV artifact 门与最少 10 个干净 epoch；不套用 PSD prepared-source |
| Theta/Beta | 是 | 是（生产开关关闭） | scalar capability 与真实格式 API/WPF 投影对照通过；等待后续 GUI 总验收 | `scalar-dynamic-window-v1`；默认/配置窗口长度；IAPF 驱动 Theta/Beta 频段 | 连续 source 后取窗；个体峰、Theta/Beta 范围、功率、dimensionless 单位和质量由模块 evidence 返回 |
| Brainbeat | 是 | 是（生产开关关闭） | capability 与完整动态 Run/真实 Recording API/WPF 投影对照通过；等待阶段 4 GUI 验收 | `brainbeat-dynamic-window-v1`；Fz/Pz 双通道，首个达到最小窗口后按游标规划；不足返回 `insufficient_data` | 连续预处理后取窗；每窗独立计算 IAPF 驱动的 Fz Theta / Pz Alpha；`cross_window_ema=false`；质量拒绝保留 null 与结构化详情 |

## 迁移准入

每个算法必须单独完成以下证据后才能把“按需单窗能力”标为“是”：

1. 明确单窗的最小窗口、warmup、尾窗、gap、质量拒绝和参考/通道依赖。
2. 明确 prepared-source 身份及预处理范围；不得因为性能目标偷偷改变滤波或 epoch 语义。
3. 用同源 fixture 按窗口对照旧完整 Run，比较 sample range、轴、单位、数值、质量、失败原因和 provenance。
4. 覆盖取消、重复 ensure、并发订阅、损坏结果和 worker 重启。
5. 只有验证报告和 OpenSpec 任务都完成后，才允许打开对应生产开关。

当前结论：PSD、RBP、FAA、Brainbeat、STFT 和四个标量模块已通过各自自动化能力/数值/API/WPF
投影门，生产开关仍关闭，尚未进行最终 GUI 验收；没有能力的算法继续旧完整 Run，不会从共享服务
获得隐式 fallback。FAA 的连续预处理契约尤其不能与 PSD/STFT 合并验证。

PSD 当前证据与测量边界见 [验证报告](../validation/2026-10-13-on-demand-psd-validation-report.md)。
后端和 WPF 全量通过不等于按需功能已打开，也不替代后续 GUI 总验收。

本次 RBP/标量逐场景对照证据见
[阶段 2 迁移证据报告](../validation/2026-10-13-on-demand-metric-migration-stage2.md)。

本次 FAA 阶段 3 证据见
[FAA 阶段 3 验证报告](../validation/2026-10-13-on-demand-faa-stage3.md)。

本次 Brainbeat 阶段 4 证据见
[Brainbeat 阶段 4 验证报告](../validation/2026-10-14-on-demand-brainbeat-stage4.md)。
