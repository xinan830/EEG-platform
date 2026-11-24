# TNK 执行策略与结果复用决策

此矩阵冻结 TNK 子指标的执行策略与结果复用语义。当前后端已注册一个公开的 `tnk_biofeedback` 组，组内 16 个 `tnk_*` 子指标仅作为内部计算模块，不单独出现在算法目录中。公开组的当前边界是动态分析；子指标表中的“全量”表示内部策略或未来可复用语义，不表示公开组已经提供静态 Run。机器可读的逐项支持/unsupported 声明和结构化原因见同目录 `tnk-strategy-matrix.json`。各能力仍须按平台 `DynamicStrategyCapabilities` 单独声明实现和验证状态；未实现能力不得对外宣称已验收。

公开组当前边界：

- `tnk_biofeedback`：已注册、可运行，仅支持 `dynamic`。
- 静态请求：在科学计算前以结构化 unsupported-mode 结果拒绝，不隐式转换为动态请求。
- 子指标：用于组级 dispatch、结果 evidence 和 provenance；不作为独立可选目录项。

| TNK 模块 | 全量 | 按需 | 组合全量+按需 | 全量结果→按需 | 按需结果→全量 |
|---|---|---|---|---|---|
| IAPF | 支持，run reset=0 | 支持；从 reset 重放 30 s 候选节拍与锁定状态 | 支持；相同上下文、网格和状态序列 | 仅复用具有完整中间状态证据且目标边界相同的窗 | 仅复用完全覆盖的同一窗和前序上下文 |
| 放松度 | 支持，run reset=0 | 支持；重放 IAPF 锁定/回退状态后计算目标窗 | 同上下文可组合 | 需同一 IAPF 状态、窗口与输入快照 | 需同一 IAPF 状态、窗口与输入快照 |
| 空间分布 | 支持，run reset=0 | 支持；重放 IAPF 状态后计算目标窗 | 同上下文可组合 | 需同一 IAPF 状态和所有角色输入 | 需同一 IAPF 状态和所有角色输入 |
| 节律稳定性 | 支持，run reset=0 | 支持；重放逐通道 10 帧历史与 readiness | 同上下文、warm-up 和窗口网格可组合 | 需可证明逐通道历史完全相同 | 需可证明逐通道历史完全相同 |
| 综合冥想指数 | 支持，run reset=0 | 支持；重放依赖指标并保留 degraded-quality 证据 | 依赖项及状态完全相同才可组合 | 需稳定性、SD 与质量状态同时完全相同 | 需稳定性、SD 与质量状态同时完全相同 |
| BrainBeat 窗口模式 | 支持，run reset=0 | 支持；重放 3 帧 log warm-up/EMA | 同上下文、warm-up 与窗口网格可组合 | 需逐窗 EMA 状态一致 | 需逐窗 EMA 状态一致 |
| BrainBeat Flat | 支持，run reset=0 | 支持；独立重放其 warm-up/EMA | 同上下文、warm-up 与窗口网格可组合 | 需本模块自己的 EMA 状态一致 | 需本模块自己的 EMA 状态一致 |
| 疲劳指数 | 支持，run reset=0 | 支持；重放逐通道历史与平滑状态 | 同上下文、通道和窗口网格可组合 | 需逐通道状态完全相同 | 需逐通道状态完全相同 |
| HAI | 支持，run reset=0 | 支持；重放声明的 PSD 更新节拍与候选状态 | 同上下文和 PSD 证据可组合 | 需同一 PSD、IAPF 候选/锁定状态 | 需同一 PSD、IAPF 候选/锁定状态 |
| FAA | 支持，显式 segment/report | 仅支持请求范围等于冻结 segment/report；任意游标窗明确不支持 | 仅同 segment 边界可组合 | 仅完全相同的 segment 可复用 | 仅完全相同的 segment 可复用 |
| 固定频段 RBP | 支持，run reset=0 | 支持；窗级独立计算 | 同输入与窗口可组合 | 仅样本边界和预处理快照相同 | 仅样本边界和预处理快照相同 |
| 1/f 非周期指数 | 支持，run reset=0 | 支持；重放声明的 30 s 拟合节拍 | 同拟合输入、状态和窗口网格可组合 | 需谱输入及拟合状态完全相同 | 需谱输入及拟合状态完全相同 |
| Crimson 神经网络冥想度 | 支持，raw Fz reset=0 | 支持；从 reset 重放到目标结束，保留 250 Hz 滤波/模型/20 点平滑语义 | 同 raw source、模型资产和目标网格才可组合 | 需完整平滑状态和 raw source identity | 需完整平滑状态和 raw source identity |
| Zenlite 平静度 | 支持，raw Fz reset=0 | 支持；从 reset 重放到目标结束，保留 256 Hz 模型语义 | 同 raw source、模型资产和目标网格才可组合 | 需完整平滑状态和 raw source identity | 需完整平滑状态和 raw source identity |
| Zenlite 觉察度 | 支持，raw Fz reset=0 | 支持；从 reset 重放到目标结束，保留 256 Hz 模型语义 | 同 raw source、模型资产和目标网格才可组合 | 需完整平滑状态和 raw source identity | 需完整平滑状态和 raw source identity |
| Zenlite 频谱放松度 | 支持，raw Fz reset=0 | 支持；从 reset 重放 FFTPACK 幅值与频带映射 | 同 raw source 和目标网格才可组合 | 需完整平滑状态和 raw source identity | 需完整平滑状态和 raw source identity |

统一约束：

- “支持”描述子指标策略语义；只有逐算法数值和状态证据才能标成已验证。当前公开组已经注册并可运行，但组级完整数值 parity、真实录音和人工验收仍未完成，不能把“已实现”写成“已验收”。
- 结果身份必须绑定原始记录 checksum、current Definition/build、科学配置、导联/滤波/质量上下文、显式 reset origin、历史 context identity、半开样本区间和窗口策略版本。
- 所有可复用方向都要求 exact identity 与完整质量/状态证据；缺少任一项时结构化拒绝复用，不静默回退、截短历史或拿 held 值代替目标计算。
- 每个模块通过自己的能力声明选择适用策略；公共调度器只读声明和通用计划，不根据 `tnk_*` algorithm ID 写条件分支。
- 所有历史重放受样本数和窗口数双预算限制；超限返回 `WINDOW_RESOURCE_EXCEEDED`，携带资源名、请求量和上限。
