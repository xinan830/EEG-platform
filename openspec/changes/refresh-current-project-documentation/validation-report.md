# 当前项目文档整理验证记录

日期：2026-10-22。基线提交：`861ba46a1d65e78cdbb789cc2bfdcdd2023d010f`。对应[任务](tasks.md)。

## 整理结果与范围

- 更新根 README、HANDOFE、文档导航、工程契约、当前能力路线图和 OpenSpec 技术栈上下文。
- 按当前代码重写算法运行方式、读取/导联/滤波、模块归属、结果与追溯说明，补齐 WPF 注册入口、官方预设生成与分析快照行为。
- 删除 63 份被替代文档：`docs/archive/` 的 56 份阶段笔记及索引、两份调研文本、一份已完成桌面实施草案、四份过时架构/执行草案。删除前内容可从 Git 查找；不建立第二套历史文档树。
- `official-algorithm-migration.md` 被生产目录元数据引用，因此保留路径并更新为当前版本边界；不通过删除它制造失效链接。
- 保留带日期的验证报告及 OpenSpec 决策记录，新增[证据索引](../../../docs/validation/README.md)。历史报告不改写测量数字；仅修复归档路径，并明确三份未入库的 `_verify` 本机证据无法从当前仓库直接重放。
- 两份主规范只添加历史适用状态和后续退役规范链接，原 requirement/scenario 保留；不以文档整理恢复旧图/公式执行器或浏览器工作台。

生产代码、算法公式、滤波实现、API/WebSocket、时间、单位、通道顺序和原始数据均未修改。唯一测试修改是删除 `test_audit_documents_legacy_deletion_gate`：它只断言已删除历史文档中的四句文案，不验证可执行行为。依赖方向、退役计算器禁用和模块边界测试均保留。

## 验证

- `backend/.venv/Scripts/python.exe -m pytest backend/tests/test_algorithm_runtime_architecture.py backend/tests/test_official_algorithm_module_boundaries.py backend/tests/test_file_size_policy.py -q`：31 passed。
- 根入口、`docs/`、本变更和两份加注主规范共 105 份 Markdown 的 160 个本地链接检查：失效目标为 0；29 份当前入口/架构/算法说明中以仓库根路径标明的代码位置检查：缺失为 0。模板路径和历史报告内旧命令不作为可执行入口。
- `backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py`：通过；已有超过 400 行的业务文件按现有登记提示，本次未增长业务文件。
- `openspec validate refresh-current-project-documentation --strict --no-interactive`：通过。
- `openspec validate --specs --strict --no-interactive`：59 passed，0 failed。这是规范格式校验，不证明每条历史 requirement 仍由当前代码实现。
- `git diff --check`：通过。
- 下列 13 件 JSON/NPZ 的实际文件 SHA-256 与本轮开始时逐件一致；Git 中这些文件没有内容差异。Windows 工作树文本可能为 CRLF，而 Git blob 为 LF，二者字节哈希不能混作“整理前后”比较。
- 本轮仅做文档和针对性架构回归，没有运行真实 EEG、完整科学套件或 WPF GUI。之前 1,049/917 的完整测试数字属于基线提交，不作为本轮新测试结果。

## 冻结件核对

均位于 `docs/validation/`；表中为整理前后相同的工作树文件 SHA-256。

| 文件 | SHA-256 |
| --- | --- |
| `2026-10-08_recording-algorithm-audit-fixed.json` | `2af12555ff24593173ad99aba4aae761f70758264f3549a4d13a48075928228d` |
| `2026-10-08_recording-algorithm-audit.json` | `0b6e53a88f4c489209a562fa8fcad82593c6d4016556e12d87df0a7b0d905b89` |
| `2026-10-13-on-demand-psd-measurements.json` | `544b4a6ac0d4c65a27f94b21fb9fdfd78181fbbcbfbab52c9a3591a4ce6df88e` |
| `2026-10-20_algorithm-audit-baseline.json` | `ca77e0ac774f6526cb835de74eca1084d4d50d561f06fe5bd62c310b4367a25c` |
| `2026-10-21-official-algorithm-real-recording-pressure.json` | `854c6d92df72ac0c844cbc49dbed91627ced95bd85e7c85817e827309574ebd3` |
| `2026-10-21-official-combined-strategies.json` | `e70eaf9c7d2b440393ba270fffa3b4c391a2827810df39099818b0efa6cb4bfb` |
| `algorithm-result-boundary-baseline.json` | `4ba19ff213d470989417ecc659b39930c5c3511a8afe3e25d0ac11b5697663fc` |
| `algorithm-result-boundary-baseline.npz` | `66e020b9656f7613f30824d126a6cc96372728335f1100f5e3ad831f3eeb7717` |
| `official-algorithm-numeric-baseline-arrays.npz` | `e317119d157ed7cb612529c27c899744af30c0026aa757b603df0a9b2ec2e490` |
| `official-algorithm-numeric-baseline.json` | `2d4c5f0c748ba2c3d9bf5d8c285c4310319422f0c267d595d1194d39ef060575` |
| `official-algorithm-strategy-baseline.json` | `0f1ce0e368f9f1b2a646535272f455db836aa236a268bd74ce7739522131651d` |
| `official-algorithm-strategy-baseline.npz` | `32b8ad9ed72dd0f03c73a2d363ca64d74670412fd9fa4d578dc9411475e7d965` |
| `official-algorithm-strategy-matrix.json` | `89982ca95cf2797a733439638b8a2c5e95f9b2a4486deb614fdc666b6a204a14` |

## 已知限制

`.github/workflows/quality.yml` 仍含指向已不存在 `frontend/` 的 Vue CI job，这是现有 CI 配置问题；本轮没有运行 GitHub Actions，也没有修改 CI。它不能作为当前 WPF 已获持续集成覆盖的证据。

OpenSpec 中历史浏览器工作台与 shadow 规范已加状态提示，条款级合并/移除并未实施；后续需求应按当前 Runtime 与退役规范判断，不能直接照历史条款恢复功能。一般数据库迁移、API 错误封装和桌面适配不在官方算法 latest-only 的删除范围。

本轮不迁移、不删除既有 Run/artifact、原始 EEG 或数据库；不新增构建产物、日志、ZIP 或真实身份信息。删除文档可由 Git 恢复，未执行提交或推送。
