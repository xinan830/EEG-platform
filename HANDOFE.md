# 脑电算法全面审计交接文档

更新时间：2026-10-20
工作目录：D:/AI/Work/brain-platform
当前 OpenSpec：audit-official-algorithms-end-to-end

## 1. 我们在做什么

用户要求审计九个官方 EEG 算法，回答“整体算法是好还是不好”，但结论必须拆成可证据化的维度，不能用单一分数或测试全绿代替科学结论。

九个模块是：PSD、STFT、Peak Frequency、IAPF、Band Ratio、Theta/Beta、RBP、FAA、离线 Brainbeat。离线 Brainbeat 与实时链路中的 IAPF-lock/EMA BrainBeat 必须分开审计，不能互相背书。

本 OpenSpec change 是只读审计和验证工作，不在本 change 中重写公式、改变公共 API 或改变科学契约。最终需要分别报告工程实现、独立数值复算、边界和失败语义、输入/版本/时间/单位/通道/质量/provenance 追溯状态，并明确哪些结论不能声称临床有效。

## 2. 重要文件位置

- 交接文档：HANDOFE.md
- OpenSpec change：openspec/changes/audit-official-algorithms-end-to-end/
- 任务清单：openspec/changes/audit-official-algorithms-end-to-end/tasks.md
- 设计：openspec/changes/audit-official-algorithms-end-to-end/design.md
- 计划：docs/validation/algorithm-audit-plan-2026-10-20.md
- 基线说明：docs/validation/2026-10-20_algorithm-audit-baseline.md
- 基线 JSON：docs/validation/2026-10-20_algorithm-audit-baseline.json
- 阶段 1 审阅：docs/validation/2026-10-20_algorithm-audit-review.md
- 工程契约：docs/工程开发契约.md
- 项目 skill：C:/Users/23998/.codex/skills/brain-research-platform/SKILL.md

Windows 环境有 PowerShell 7，后续优先使用 pwsh 读取和处理中文文件，避免编码显示问题。

## 3. 已经完成了什么

### 3.1 前期结果链路工作

- PSD/STFT 的 Full Run 与按需窗口读取已修复并由用户实际验收。
- Full Run 结果优先从已保存 artifact 读取，不应在游标移动时重新计算。
- PSD/STFT 使用结构化窗口适配器；标量和多指标算法使用另一种共享适配器；FAA/Brainbeat 保留算法专属 evidence 校验。
- Full 读取失败不得静默回退成按需计算，否则会掩盖 provenance 错误。
- 工作区存在上一轮未提交的 WPF 和测试改动，不能 reset、清理未跟踪文件或覆盖用户数据。

### 3.2 阶段 0–1

OpenSpec 阶段 0 基线和阶段 1 静态契约/实现审阅已经完成：

- 九个算法均有稳定 ID、scientific version、implementation identity、输入/输出 schema 和静态/动态入口。
- 已审阅 manifest、config、runner、compute/official、runtime registry、OpenSpec 规范和主要测试。
- 已记录时间、样本范围、通道顺序、参考、单位、滤波、质量门、失败码、evidence、artifact 和 provenance。
- 已把 IAPF 非正/非有限 PSD、Theta/Beta 频段越界等记录为风险，而不是擅自改公式。
- openspec validate audit-official-algorithms-end-to-end --strict 已通过。

### 3.3 第 3 章自动化边界和回归测试

第 3 章 3.1–3.6 已完成并已在 tasks.md 勾选。

新增测试文件：

- backend/tests/test_official_algorithm_boundary_conditions.py
- backend/tests/test_official_algorithm_extended_audit.py

覆盖内容：

- IAPF 拟合点不足、平坦谱、非正/非有限 PSD、Alpha 无 bin、IAPF 边界；
- Theta/Beta 个体化频段为空或越界时的 runner 结构化失败；
- PSD/Welch 频率轴、单位、矩阵维度、质量门、缺口拒绝和不插补；
- STFT 线性功率/dB 单位、坏帧 NaN、窗口中心、动态 warm-up；
- Peak Frequency 包含边界和并列峰；
- Band Ratio 边界插值梯形积分、零/非有限分母；
- RBP Gamma 覆盖、五频段比例和 official-rbp-v2 身份；
- FAA F3/F4 角色、同源通道拒绝、成对 epoch 质量、参数顺序和符号关系；
- 离线 Brainbeat Fz/Pz 角色、缺失通道 null、执行顺序和跨窗口独立性。

测试证据：

- 第 3 章相关后端测试：137 passed；
- 全量 backend/tests：816 passed；
- 保留 5 条预期 RuntimeWarning：退化 PSD 输入触发 log10/nanmean 警告；测试确认结果不可用或保持结构化失败。
- git diff --check 未发现空白错误；只报告了现有文件的 LF/CRLF 转换提示。

桌面测试状态：

- 已尝试运行 dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --verbosity minimal。
- 已成功构建并启动测试程序集，但会话中断前没有拿到最终测试汇总。
- 因此不能把桌面测试写成通过；新会话应重新运行并记录最终结果。

## 4. 当前 OpenSpec 进度

总任务 34 个，已完成 16 个，剩余 18 个：

- 1.1–1.4：完成；
- 2.1–2.6：完成；
- 3.1–3.6：完成；
- 4.1–4.5：未完成；
- 5.1–5.4：未完成；
- 6.1–6.4：未完成；
- 7.1–7.5：未完成。

当前 change 状态为 ready，尚未归档。

## 5. 已确认但尚未解决的风险

### IAPF

直接把负、零或非有限 PSD 传给 estimate_iapf 会产生 NumPy warning，并可能得到带 nan diagnostics 的不可用结果。

生产频谱路径通常会把 PSD 下限限制为正值，所以目前最准确的说法是“输入契约/边界防护风险”，不是已经证明的用户路径 bug。后续应在独立 change 中决定：estimate_iapf 内显式拒绝并返回结构化失败，或者由上游强制保证正值并把约束写入正式契约。

### Theta/Beta

低层 metric_values 在 IAPF 接近边界、个体化频段倒置或超出频率轴时可能抛 ValueError；正式 compute_theta_beta runner 会将其转换为 BAND_RANGE_INVALID 结构化失败。不要把低层 helper 的异常直接描述成用户路径未处理异常，除非后续测试证明 runner 路径确实遗漏。

### 其他限制

- Peak Frequency、Band Ratio、Brainbeat 从原始 EEG 到最终指标的完整独立复算仍不完整。
- 真实记录验证数据集、授权范围、采样率和通道覆盖尚未确认。
- 没有授权真实数据时，不能伪造“真实数据通过”。
- 工程测试、合成信号复算和图表显示都不能推出临床有效性。

## 6. 下一步计划

严格按 tasks.md 顺序继续。

### 阶段 3：独立数值参考

1. 先重新运行桌面结果链路测试，记录最终通过/失败和环境限制。
2. 设计不导入生产科学函数的独立 PSD/Welch、STFT/FFT、band-power 参考实现。
3. 冻结 NumPy/SciPy 版本、rtol/atol、最大绝对/相对误差和失败坐标报告格式。
4. 为 Peak Frequency、IAPF、Band Ratio、Theta/Beta、RBP、FAA、离线 Brainbeat 做原始输入或独立频谱复算。
5. 大数组只能保存为受控 ValidationRun/artifact；源码、Markdown、SQLite 摘要和前端状态只保存摘要、shape、单位、checksum 和身份信息。

### 阶段 4：动态与稳健性

- 故障注入：坏段、缺样本、尖峰/饱和、过短记录、非有限输入、取消、部分失败、缓存/历史 Run 身份不一致。
- 核验每个动态点的 start/end、样本边界、锚点、warm-up、质量状态和后续窗口继续策略。
- 不可用结果必须保持结构化状态和 null，不能变成零、旧值、插值或静默回退。
- 实时 BrainBeat 的 IAPF-lock/EMA 必须单独设计状态测试，不使用离线 Brainbeat 结果替代。

### 阶段 5：授权真实数据

开始前先登记数据授权、去标识化 recording ID、文件字节哈希、采样率和通道覆盖、代表性状态和排除规则。然后固定通道映射、参考、滤波顺序、绝对时间范围、窗口计划、scientific version、implementation identity 和配置哈希，分别运行平台与独立参考。

### 阶段 6–7：评级和收尾

- 每算法报告工程实现、独立复算、边界覆盖、来源追溯四项状态和 A/B/C/D 评级；
- 建立缺陷、性能风险和科学决策 backlog；
- 契约违例或公式变更另开 OpenSpec change；
- 更新验证报告索引，明确不把工程/数值通过写成临床结论；
- 最后运行 OpenSpec strict validation、相关后端/桌面测试和 git diff --check。

## 7. 重要经验和约束

1. 不要把“算法有图”“HTTP 200”“测试全绿”当成科学正确性的证明；必须检查返回值、单位、时间、来源和质量证据。
2. 不要把九个算法强行统一成一个公式。统一身份、时间/样本、单位、状态、错误和 provenance；公式、通道关系、输出维度和算法专属 evidence 保持独立。
3. 不要把离线 Brainbeat 与实时 EMA/IAPF-lock BrainBeat 混为一谈。
4. 前端不得重算 EEG、PSD、频谱或指标；独立参考只能在后端验证侧运行。
5. 比较结果必须固定源文件、通道顺序、参考、滤波顺序、采样率、绝对范围、窗口计划、科学版本、实现身份和配置哈希。
6. null 是不可用语义，不得用零、上一窗口结果或静默回退掩盖失败。
7. 业务文件以 400 行为软上限；当前边界测试已拆成两个文件，分别为 189 行和 377 行。
8. 大数组和真实 EEG 不得写入源码、Markdown 或前端状态。
9. 用户偏好中文、直接推进；只有当数据授权或科学定义会实质改变范围时才询问。
10. 不要声称完成 GUI 人工验收，除非用户明确验收。

## 8. 新会话第一步

1. 读取本文件、tasks.md、design.md、2026-10-20_algorithm-audit-baseline.md 和 2026-10-20_algorithm-audit-review.md。
2. 用 PowerShell 7 运行 openspec instructions apply --change audit-official-algorithms-end-to-end --json。
3. 重新运行桌面测试并拿到最终汇总：
   dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --verbosity minimal
4. 从任务 4.1 开始，不要重新盘点九个算法，也不要直接重写 IAPF 或其他公式。
