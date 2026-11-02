## Context

本 change 是一次只读审计和验证工作，基于九个已注册离线官方算法、各自 manifest/config/runner/compute、现有 runtime provenance 以及已有 pytest 验证。项目要求科学计算只在后端完成，前端只展示后端值；现有工程验证与临床有效性必须分开表达。审计期间不得把疑点直接改成公式或公共契约变化。

## Goals / Non-Goals

**Goals:**

- 建立一个可复查的九算法基线：版本、输入、时间/样本、单位、窗口、质量和证据。
- 让边界测试和独立参考验证从同一组固定身份和参数运行，并能报告逐点/逐窗误差。
- 将失败、不可用、warm-up、通道角色和历史版本兼容性作为一等验证结果。
- 形成有证据链接的逐算法评级和后续变更入口。

**Non-Goals:**

- 不在本 change 中修改算法公式、滤波器、质量门、API 或桌面结果展示。
- 不把通过工程测试或数值复算解释为临床/诊断有效。
- 不把离线 Brainbeat 与实时有状态 BrainBeat 合并，也不启用实时链路。
- 不提交真实 EEG、原始样本、文件名、参与者身份或大数组到源码和 Markdown。

## Decisions

1. **以现有 manifest 和 OpenSpec 为身份源。** 每次审计记录 algorithm ID、Definition/scientific version、implementation identity、配置摘要和输入 artifact 哈希；不从 UI 名称或“最新版本”推断。这样能避免历史 Run、缓存和新实现被混比。备选方案是只按算法名比较，成本较低但无法证明科学身份一致，因此不采用。

2. **分四层验证。** 先做契约/代码静态审阅，再做合成信号和边界测试，再做独立原始输入复算，最后才做授权真实记录端到端验证。每层单独出结论，上一层失败时不把下一层结果包装成通过。备选方案是直接跑真实记录，容易把输入、参数和参考实现差异误判为算法错误。

3. **独立参考不得调用生产科学函数。** PSD/STFT 使用独立 NumPy/SciPy 公式；指标参考从独立频谱或原始样本重新计算，并分别报告中间量。对已有参考测试只审查其是否真正独立，不把同一函数的重复调用算作独立证据。

4. **验证结果采用小摘要加不可变 artifact。** 报告保存身份、配置、轴、单位、容差、误差、失败坐标和 artifact checksum；大数组只进入受控验证 artifact。这样兼顾复现和文件规模，避免把 EEG 或大矩阵写进源码、Markdown、SQLite 摘要或前端状态。

5. **失败语义优先于数值完整。** 对缺失数据、质量门失败、频段覆盖不足和动态未成熟窗口，验证预期是结构化状态和 `null`，不是零、旧值、插值或静默回退。边界测试同时检查状态、failure code、实际范围和 provenance。

6. **缺陷与科学决策分轨。** 已批准契约的可重复违例建立实现修复；契约含义不明或公式需改变时建立新的 OpenSpec scientific-contract change，不在审计分支中直接“修正”。

## Risks / Trade-offs

- **[Risk] 合成信号通过但真实数据失败** → 将合成验证标为工程/数值层证据，另设授权真实数据阶段，不提升评级。
- **[Risk] 独立参考与生产实现共享同一错误** → 参考实现不导入生产计算函数，保留公式、库和版本记录，并做代码审阅。
- **[Risk] 真实数据不可用或授权不足** → 停在未验证状态，记录阻塞原因，不伪造数据集通过。
- **[Risk] 容差被人为调宽掩盖差异** → 先冻结 `rtol/atol` 和比较规则；差异必须解释为契约、浮点、输入或实现问题。
- **[Risk] 历史版本与当前版本混比** → 所有比较强制检查科学版本、实现身份、配置哈希和源 artifact；历史结果只读，不迁移。
- **[Risk] 审计过程扩大成重构** → 代码修改必须另开 change；本 change 的任务只允许新增验证材料、测试和报告。
- **[Risk] IAPF 数值退化被压成通用 unavailable reason**（任务 2.6 登记，2026-10-20 代码审阅发现）→ `app/algorithms/iapf/official.py::estimate_iapf` 在 `gate_failed` 为空且 PSD 含非正/非有限值时仍直接执行 `np.polyfit`：helper 结果为 `value=None, gate_failed=None` 并产生 `divide by zero`/`invalid value` RuntimeWarning；外层 `compute_iapf` 会返回通用 `IAPF_UNAVAILABLE`，所以并非完全没有失败码，但调用方无法区分输入域无效与“没有可信峰/重心”。后续如契约负责人要求可区分的 reason，应另开 change 增加显式输入防护及结构化原因。
- **[Review correction] IAPF 有限退化谱的 peak_hz NaN 假设未复现，不作为已确认风险**（2026-10-20 复核）→ 直接用当前 NumPy/生产函数计算 1–30 Hz、0.25 Hz 栅格的正有限合成平坦 PSD 与 1/f PSD，二者均返回有限 `peak_hz=7.0`、有限 COG（约 10 Hz），source 为 `cog`；浮点误差使残差并非严格零。当前没有证据支持“有效有限输入会暴露 NaN peak_hz”的说法。非正/非有限 PSD 路径的 warning 与失败原因可区分性仍保留为前一条风险；若日后发现非有限 evidence 实际越过 API 序列化边界，再按具体复现建项。

## Migration Plan

不部署运行时迁移。按任务顺序生成审计矩阵、测试、独立参考和报告；若发现契约违例，先记录并暂停该算法评级，再通过独立 change 修复。修复后的新 scientific version、兼容策略和历史 Run 读取由后续 change 负责。

## Open Questions

- 真实记录验证所用数据集、授权范围和覆盖组合需要在阶段 5 开始前由项目负责人确认；这不影响前四阶段的代码和合成验证。
