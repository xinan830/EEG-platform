# 算法执行边界整理：收尾与验收

日期：2026-10-22。变更：`clarify-algorithm-execution-boundaries`。

第 1–4 章的实现和自动化回归已经完成；第 5 章整理当前架构、作者指南、规范及提交范围。用户于 2026-10-22 确认修复后的界面可用，并明确同意验收提交。实际验收由用户完成，本文不推断未逐项描述的点击细节。

## 当前边界

- 输入由 typed signal request/source 描述，显式携带有序角色、采样率、单位、绝对样本范围、导联快照和处理身份。FAA 配对时序、Brainbeat 配对频谱和其他频谱输入保留自己的科学语义。
- 算法声明输入/输出与策略能力，独立负责公式和质量门；公共层负责调度、身份、取消、结果校验、序列化及 artifact 持久化。
- 结果根版本为 `official-algorithm-result-v1`；标量动态版本为 `dynamic-analysis-frame-v6`，使用 `output.value` 和显式状态。无官方旧格式 reader、转换器、双写或专属 UI。已有 Run/artifact 与原始 EEG 字节不迁移、不删除。
- 本范围未删除通用 API 错误 `detail`、数据库迁移或服务同步/队列适配，不能表述为全项目没有兼容代码。

当前说明见[架构](../architecture/traceable-analysis-foundation.md)、[作者指南](../algorithms/official-algorithm-development.md)、[执行清单](../algorithms/official-algorithm-execution-inventory.md)。三个 delta spec 已同步到对应主规范，保留其他能力的要求。

## 自动化证据

最初文档收尾沿用同日第 4 章完成后的全量测试。随后用户在 5.4 发现官方预设候选遗漏，本轮修改共享 WPF 导联目录及回归测试，重新执行桌面套件；后端代码与科学实现未因本次修复改变，后端证据继续使用第 4 章结果。原始机器日志在被忽略的 `.artifacts/`，不提交机器账户信息。

| 检查 | 结果与本地证据 |
| --- | --- |
| 完整后端 | 1,049 passed，0 failed；5 条已有 NumPy 边界输入 warning；`chapter4-full-backend.xml/.log` |
| WPF 完整测试与构建 | 官方预设修复后 917 passed，0 failed，0 skipped；`chapter5-tests/chapter5-full.trx`，构建输出在 `chapter5-build/`；含新增 5 项目录回归 |
| 官方预设修复回归 | 修复前两个新用例失败，复现空用户目录遗漏预设；修复后导联及 HTTP 客户端相关 132 passed，0 failed，见 `chapter5-tests/chapter5-montage-before.trx` / `chapter5-montage-after.trx` |
| 架构边界 | `test_algorithm_runtime_architecture.py` 25 项；`test_scientific*` 16 项，通过 |
| 策略与方向性复用 | combined 27 项、directional reuse 6 项、strategy matrix 8 项，通过 |
| 独立数值参考 | spectral reference 3 项、official independent references 7 项、numeric reference audit 5 项，通过 |
| 冻结结果回放 | 36 场景、58 数组、172,448 点；当前摘要 5,758 个数值字段；最大绝对/相对误差均为 0，失败位置为 0 |
| 本轮收尾检查 | 变更 strict 通过；主规范 59 passed / 0 failed；规模策略和 `git diff --check` 通过 |

上述分组取自同一完整 JUnit，不应与 1,049 相加。冻结 NPZ 为 525,506 字节，SHA-256：`66e020b9656f7613f30824d126a6cc96372728335f1100f5e3ad831f3eeb7717`。本次不重建冻结数组；数值容差 `rtol=1e-10`、`atol=1e-20`，轴/坐标精确比较。具体证据与复跑命令见[第 4 章报告](current-algorithm-result-contract-validation.md)。

覆盖范围包括九算法的当前静态/动态结果、已声明策略和复用方向；不声称穷举全部导联 × 滤波 × 缺失数据组合，也不把工程验证当作临床验证。

## 文件规模与依赖

除主规模脚本外，单独对本变更涉及的 95 个 Python/C#/XAML 等源码和测试文件执行同一 `assess_file` 规则，弥补默认脚本只扫描后端生产与 Vue 源码的范围。超过 400 行的文件仅以下四个，均在 `docs/code-size-policy.json` 登记且低于已评审上限；无本次新增的超限文件。

| 文件 | 物理行数 / 上限 | 保留依据 |
| --- | --- | --- |
| `backend/app/services/run_queue.py` | 435 / 450 | 队列状态、覆盖门禁及持久化事务边界；科学执行与序列化已分离 |
| `backend/app/services/runs.py` | 401 / 500 | Run 生命周期；typed 校验/序列化、repository 和 worker 分离 |
| `backend/tests/test_official_algorithm_runs.py` | 446 / 450 | Run 生命周期集成夹具；当前格式测试已单独归属 |
| `desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfflineAnalysisSelectionTests.cs` | 483 / 490 | 同一离线选择流程；通用格式校验已单独归属 |

新 signal/result 契约、验证器、serializer 和 WPF 当前格式消费者均低于目标线。架构测试约束 Runtime 不导入具体算法和服务实现，通用结果序列化不根据算法 ID 解释科学含义；未为压缩行数拆散状态所有者。

## 5.4 用户实际验收：已确认通过

本轮已发现并修复候选目录缺陷，用户复验通过：算法页只加载持久化的用户导联，漏掉设置页动态生成的官方预设。`RecordingMontageCatalog` 现在基于记录保存的官方 ANT 通道快照生成系统候选，与用户配置合并；同 ID 的当前设备配置不能覆盖记录映射。所有候选仍经过源通道、单位、硬件参考和映射兼容性检查，算法注册仍经后端校验并保存独立分析快照。测试覆盖用户目录有/无系统预设、当前映射冲突、缺失 M1 及缺失记录快照；实际记录是否显示全部六套取决于其源通道是否满足各预设。原始记录不写入、不补造通道。

使用当前代码启动匹配的后端与 WPF，避免旧进程或旧构建混用。选一条你有权使用、具备相应源通道和兼容导联配置的记录；不需要重新验收九算法的所有参数组合。

1. **单通道标量**：选 Peak Frequency 或 IAPF。先用采集默认导联计算，再切换兼容导联，确认“分析导联”选项同步变化、所选通道合法，重新请求后结果/单位/实际范围和 provenance 对应新选择。检查静态和按需窗口，旧结果不能冒充新选择的结果。
2. **配对算法**：选 FAA，使用具有 F3/F4 角色的配置及足够长度（例如 30 秒）的有效片段。确认成对输出映射、范围和 provenance 正确；切换另一套兼容配置后重新计算。缺少必需角色的配置应明确不可用或报错，不应自动替换通道。
3. **结构化算法**：选 PSD 或 STFT。运行动态全量和组合模式，检查矩阵/曲线及频率轴、时间轴、单位；移动游标检查窗口预览；切换导联后结果和追溯证据跟随更新，旧请求不覆盖新选择。
4. **失败与追溯**：用界面允许的无效参数或不足范围触发失败，确认展示原因及范围，没有伪造零值或保留旧成功结果作为当前结果；能从结果详情追到 Run/窗口、导联、通道、处理配置和实际范围。不可用科学值显示为空及原因。
5. **当前格式**：界面不提供旧格式兼容开关或转换流程。格式非法结果沿普通结构化错误路径展示；不需要为验收制造、修改或删除历史 Run。

切换导联后“曲线一定变样”不是通过条件：等价投影可能得到相同数值。验收重点是实际请求、输入快照与返回身份正确；数值正确性由独立参考和冻结逐点比较支持。

用户可反馈“上述项目通过”，或提供失败步骤、算法、策略、范围及 request_id/Run 标识供本地排查。共享证据应去掉身份信息；不将真实 EEG、带身份截图或日志纳入提交。

## 已验收提交

候选仅包含本变更的后端/WPF 源码、测试、文档、OpenSpec，以及受控合成基线 JSON/NPZ。合成 NPZ 是有 checksum 的科学回归夹具，不是运行缓存。

已检查提交路径没有 `.artifacts/`、`bin/obj`、DLL/PDB/BAML、ZIP、日志、数据库或 EEG 文件；忽略规则保护本地构建与运行产物。

5.4 已按用户 2026-10-22 的明确反馈勾选通过，5.5 已完成。验收提交为 `861ba46a1d65e78cdbb789cc2bfdcdd2023d010f`（`整理算法执行边界并验收官方导联预设修复`），已提交推送。该 change 为 22/22，主规范已同步，变更记录尚未归档；不再把验收提交列为待办。
