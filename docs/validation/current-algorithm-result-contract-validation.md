# 当前官方算法结果契约验证（第 4 章）

日期：2026-10-22。对应 `clarify-algorithm-execution-boundaries` 4.1–4.4。本文为工程契约验证，不是临床有效性结论；只使用确定性合成输入和临时数据库，不读取真实 EEG 或个人身份信息。

## 当前行为与删除范围

- 已完成官方 Run 的结果根版本固定为 `official-algorithm-result-v1`；标量动态摘要和逐点版本固定为 `dynamic-analysis-frame-v6`。后端发布前校验，Run API、结果查看、预览、导出、provenance 及 WPF 消费者只接受当前格式。
- 标量值只在 `output.value` 提供，允许显式 `null`，单位必须存在。删除重复顶层 `value`、`warmup`、运行时缺失状态推导及 WPF 多格式回退。RBP 的五频带 `band_values` 仍是当前多指标输出，不属于重复主值。
- 标量动态点必须显式包含 `analysis_state`、窗口范围、窗口结束时间和 quality；PSD/STFT 动态结构化窗口显式包含 `state`、样本范围、时间范围和 quality。轴必须引用长度匹配的一维数组，数组单位及非负整数形状必须存在。
- 模块输出不合法在执行边界以 `ALGORITHM_RESULT_INVALID` 失败，不能发布为完成结果。不合当前格式的已完成官方结果在读取边界统一 HTTP 409 / `ALGORITHM_RESULT_INVALID`；不按旧格式增加分类、转换或专属 UI，读取不重算科学结果。
- 原始记录、已存 Run JSON 和 artifact 字节不迁移、不删除、不改写。没有旧格式 reader、双写或生产转换器。数据库 schema migration、Run 服务同步/队列接口适配不在本章结果清理范围。`allow_warmup` 仍是当前 Partial 窗口的科学规划选项，不是结果格式回退。

## 自动化证据

`backend/tests/test_current_algorithm_result_contract.py` 通过真实 PersistentRunQueue 对九算法分别产生静态和动态结果，验证 GET Run/result、provenance、ZIP/NPZ 导出，以及 PSD/STFT 的结构化和保存窗口预览。对比读取前后的持久 JSON 与 artifact checksum，确认没有写入。当前与不支持格式测试检查 root/frame 版本、重复值、缺失状态/单位/范围、非有限值、轴引用/长度、数组形状和窗口元数据；无效结果在数组解码前拒绝。

`backend/tests/test_execution_migrations.py` 不再断言旧 FAA 结果可解释，而验证其被当前 schema 拒绝；读取不执行计算、不解码数组、不改动数据库或 artifact。数据库升级测试仍负责存储结构演进；模拟旧数据库时移除当时尚不存在的导联快照表，没有修改生产 migration 来绕过夹具错误。动态 coverage 导出测试使用真实当前结果，只破坏 coverage，确保失败来自覆盖门禁。

WPF 的 `CurrentAlgorithmResultTests` 验证 HTTP Run 边界和标量/结构化当前格式，确认缺失或不支持的字段直接失败且不修改载荷。各算法详情、全量转窗口和显示测试已改为当前结果夹具；没有为旧夹具保留生产解析路径。

## 冻结数值回归

沿用 [第 3 章冻结基线](algorithm-result-boundary-validation.md)：36 场景覆盖九算法静态/动态正常及高幅值输入。冻结 NPZ 保持 525,506 字节，SHA-256 为 `66e020b9656f7613f30824d126a6cc96372728335f1100f5e3ad831f3eeb7717`，不由本次实现重新生成。

`test_result_boundary_baseline.py` 仅在测试预期中投影批准的 wire 变动：添加根版本、v5 改 v6、确认重复主值与 `output.value` 相等后删除重复字段、删除 warmup。该函数不在生产路径中；不改变任何冻结数组、科学数值、坐标、状态、质量或 provenance。科学数值使用原 `rtol=1e-10`、`atol=1e-20`，频率/时间坐标精确相等；正常动态数组另与原 21 个冻结数值数组交叉比较。

本轮完整回归：后端 1,049 项通过（267.08 秒），WPF 912 项通过，均无失败。后端有 5 条来自非有限/非正数边界输入测试的 NumPy warning；不属于结果格式校验失败，也未修改这些科学公式来消除 warning。

从本轮 JUnit 的逐案例诊断核对：36 场景、58 个数值数组、172,448 个数组点，最大绝对误差和相对误差均为 0，失败坐标为 0。当前摘要比较 5,758 个数值字段，最大绝对/相对误差均为 0，失败位置为 0。第 3 章的 5,910 项包含本章移除的 152 个重复顶层数值主值；测试预期投影在删除前逐项确认其等于 `output.value`，没有减少独立科学数值或数组的覆盖。

冻结 NPZ 的字节数及 SHA-256 与基线一致。OpenSpec strict、文件规模策略与 `git diff --check` 均通过；本章触及的超过 400 行文件已有规模评审登记。

## 重跑入口与边界

```powershell
python -m pytest backend/tests -q --tb=short -o junit_family=legacy --junitxml=.artifacts/chapter4-full-backend.xml
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --artifacts-path D:/AI/Work/brain-platform/.artifacts/chapter4-build --logger "trx;LogFileName=chapter4-full.trx" --results-directory .artifacts/chapter4-tests --verbosity quiet -p:NuGetAudit=false -p:NoWarn=NU1701
openspec validate clarify-algorithm-execution-boundaries --strict
python backend/scripts/check_file_sizes.py
git diff --check
```

日志、JUnit、TRX 和构建产物放在被忽略的 `.artifacts/`，不作为源码提交。验证覆盖当前结果清理与已有全量/按需/combined/方向性复用回归，未穷举全部导联 × 滤波 × 缺失数据组合，也不替代桌面实际点击验收。最终架构/作者指南、验收与提交仍由第 5 章跟踪。
