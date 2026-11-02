## 1. 建立审计基线

- [x] 1.1 汇总九个算法的 manifest、Definition/scientific version、implementation identity、可运行状态和历史/实时边界，生成版本化基线矩阵。
- [x] 1.2 为每个算法登记输入通道和顺序、参考、V/float64 输入、输出单位、请求/实际秒数与半开样本范围、静态/动态窗口及 warm-up 规则。
- [x] 1.3 登记每个算法的滤波、Welch/FFT/积分参数、质量门、失败码、算法专属 evidence、artifact 与 provenance 字段。
- [x] 1.4 将已有测试、OpenSpec 条款和既有验证报告映射到矩阵，分离“已证实”“待测”“科学定义待确认”。

## 2. 契约和实现审阅

- [x] 2.1 审阅 PSD 和 STFT 的频率轴、单位、时间轴、窗口边界、坏帧/坏段处理及 PSD/STFT 差异。
- [x] 2.2 审阅 Peak Frequency、Band Ratio、IAPF 和 Theta/Beta 的公式、频段边界、IAPF 依赖、分母/无结果语义。
- [x] 2.3 审阅 RBP 的五频段、归一化分母、Gamma/50 Hz 陷波、Nyquist 要求和历史四频段兼容。
- [x] 2.4 审阅 FAA 的 F3/F4 成对来源、预处理次序、epoch 质量门和对数差公式。
- [x] 2.5 审阅离线 Brainbeat 的显式 Fz/Pz、逐窗 IAPF、无跨窗状态，并与实时 BrainBeat 分开记录。
- [x] 2.6 记录 IAPF 拟合点不足、非有限/非正 PSD、退化拟合以及 Theta/Beta 频段越界等疑点；只形成风险条目，不在本 change 改公式。

## 3. 自动化边界和回归测试

- [x] 3.1 为九个模块补齐参数顺序、缺失/重复通道、采样率/Nyquist、频率覆盖、短范围和无效数值测试。（backend/tests/test_official_algorithm_boundary_conditions.py；相关边界测试均通过）
- [x] 3.2 为 IAPF/Theta-Beta 增加拟合退化、Alpha 无 bin、IAPF 靠近边界和实际频段为空/越界测试。（backend/tests/test_official_algorithm_extended_audit.py）
- [x] 3.3 为 PSD/STFT 增加频率轴、单位、矩阵维度、质量门、缺口不插补和动态实际范围/warm-up 测试。（backend/tests/test_official_algorithm_extended_audit.py）
- [x] 3.4 为 Peak Frequency/Band Ratio/RBP 增加边界 bin、并列峰、积分边界、零/非有限分母、Gamma 覆盖和历史版本测试。（backend/tests/test_official_algorithm_boundary_conditions.py；backend/tests/test_official_algorithm_extended_audit.py）
- [x] 3.5 为 FAA/Brainbeat 增加通道角色、成对质量、执行顺序独立性、失败 null 和邻窗隔离测试。（backend/tests/test_official_algorithm_extended_audit.py）
- [x] 3.6 运行相关后端测试及现有桌面结果链路测试，记录环境、命令、通过/失败和已知限制。（相关后端 137 passed；全量 backend/tests 816 passed；未声称 GUI 人工验收）

## 4. 独立数值参考

- [x] 4.1 设计不导入生产计算函数的独立 PSD/Welch、STFT/FFT 和 band-power 参考实现，冻结库版本及 `rtol/atol`。（analytic-numpy-reference-v1；NumPy 2.3.4、SciPy 1.16.3；精确轴比较，数值按逐类型容差）
- [x] 4.2 为 Peak Frequency、IAPF、Band Ratio、Theta/Beta、RBP 建立从独立谱/原始输入到指标及中间量的参考比较。（参考实现及覆盖见 docs/validation/2026-10-20_official-algorithm-independent-reference.md）
- [x] 4.3 为 FAA 建立 paired-epoch FFT 参考，为离线 Brainbeat 建立逐窗 Fz/Pz/IAPF 参考；验证不同执行顺序不影响结果。（同一合成窗按正序/逆序单独执行并逐窗口比较）
- [x] 4.4 使用可解析合成信号逐点/逐窗比较频率轴、矩阵、指标、中间量、状态、实际范围和 provenance，输出最大误差及失败位置。（PSD/Welch 和 STFT 的实测最大误差及坐标写入阶段 3 报告，并由回归测试固定；混合 atol+rtol 判据无失败点）
- [x] 4.5 将可重放摘要和大数组分别保存为受控 ValidationRun/artifact，记录 checksum、环境和参考实现身份，不保存真实身份信息。（验证测试在临时 SQLite ValidationRun 与 NPZ 中存放，不提交信号数组；详见阶段 3 报告）

## 5. 动态与稳健性验证

- [x] 5.1 对坏段、缺样本、尖峰/饱和、过短记录和非有限输入执行故障注入，确认 rejected/unavailable/null 和结构化原因。（新增 test_algorithm_robustness_audit.py；既有动态 API 还核验缺口/质量拒绝和后续窗口恢复）
- [x] 5.2 核验每个动态点的 start/end、锚点、样本边界、warm-up、质量状态和后续窗口继续策略。（覆盖 full/on-demand 逐窗结果、endpoint anchor、半开样本范围及 rejected/gap 窗口；见阶段 4 报告）
- [x] 5.3 核验取消、部分失败、缓存/历史 Run 读取和结果 artifact 身份不一致时不会静默产生数值。（覆盖取消隔离、部分发布、损坏/过期 artifact、stale lease、cache identity mismatch）
- [x] 5.4 单独记录实时状态性 BrainBeat 的 IAPF-lock/EMA 验证需求，不使用离线 Brainbeat 结果替代。（明确状态转移、窗口、锁定/解锁、EMA、重置和 provenance 的单独验证需求；见阶段 4 报告）

## 6. 授权真实数据端到端验证

- [x] 6.1 已完成：负责人明确授权只读复用同一去标识历史记录；本地核验来源摘要、原始读取器逐样本一致、149982 样本/1000 Hz/28 通道、无缺口/非有限输入。脱敏摘要位于 `.artifacts/official-algorithm-real-record-audit-2026-10-20.json`，不纳入 Git。
- [x] 6.2 已完成（限定覆盖）：当前九算法身份/执行快照冻结，并对 F3/O1 单通道、F3/F4 FAA 与 Fz/Pz Brainbeat 的 32 组静态/动态配置执行独立参考；结果/谱坐标/质量/时间总计分别 407456/730803/4698/6376 项，失败均为 0。未穷举全 21 路动态通道。
- [ ] 6.3 未完成：后端独立复算通过，但尚未将当前九算法 artifact 与 API 返回逐项核对身份、checksum、状态和实际范围；不以历史报告替代本次证据。
- [ ] 6.4 未完成：尚未核对当前版本桌面结果展示链路，也未做本次 GUI 人工验收。

## 7. 评级、报告和后续变更

- [x] 7.1 为每个算法给出工程实现、独立复算、边界覆盖、来源追溯四项状态和 A/B/C/D 评级，明确覆盖与限制。（阶段评级见 docs/validation/2026-10-20_official-algorithm-audit-rating-backlog.md；阶段 5 未完成，评级暂定且不含临床效度）
- [x] 7.2 按严重性建立缺陷、性能风险和科学决策 backlog；每项包含证据、影响、修复优先级、验收和回滚策略。（A-01–A-06；当前没有经证实契约违例，不创建空修复 change）
- [x] 7.3 对契约违例或公式变更分别创建后续 OpenSpec change，保留历史 Run 读取和版本迁移方案。（审计未确认现行契约违例；待科学决策项明确要求另开 change，并记录版本/历史兼容策略）
- [x] 7.4 更新审计计划/验证报告索引，确认不把工程或数值通过写成临床结论。（阶段 3/4/5/评级报告互相链接；本任务同时将 6.1–6.4 保持未完成，真实数据门槛不作虚假通过）
- [x] 7.5 运行 `openspec validate audit-official-algorithms-end-to-end --strict`、全量后端/桌面测试和 `git diff --check`。（隔离冻结参考环境 backend: 826 passed, 7 warnings；WPF: 841 passed，带既有 NU1701/xUnit warnings；OpenSpec strict valid；diff check passed。6.3–6.4 尚未完成，change 不得归档。）
