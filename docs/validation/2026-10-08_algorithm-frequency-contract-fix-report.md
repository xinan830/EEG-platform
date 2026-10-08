# 算法频率契约修复与真实记录复验

## 结果

修复 [原始审计](2026-10-08_recording-algorithm-audit-report.md) 的 F-001、F-002、F-003。按 Brain Research Platform 规范保留既有公式和科学版本，仅纠正参数域、完整覆盖检查和执行快照。按 docs-generator 的技术报告规范分开记录原始发现与修复证据。

峰频率与频段功率比仍只支持冻结的 1–30 Hz 契约，不是已经扩展到 50 Hz。PSD/STFT 的可调节高低通及陷波未受此限制影响。未修改 WPF 布局、原始记录及历史 Run，未提交或推送。

## 具体修复

| 原发现 | 修复行为 | 回归证据 |
|---|---|---|
| F-001 超出频谱范围／静默部分搜索 | 峰频率和功率比配置、参数 schema 统一为有限 1–30 Hz 边界；静态、动态越界请求在入队前拒绝；输入谱轴不完整时不发布 clean 峰值 | 25–40、35–40 Hz 选峰和 30–50 Hz 分母均在读取信号前拒绝 |
| F-002 将选峰范围当滤波 | 公共 snapshot 不再从配置字段名猜滤波；选峰 8–13 Hz 单独记录，实际滤波仍写 1–30 Hz | 静态／动态与 queued、completed、重新读取的 Run 快照测试通过 |
| F-003 显式陷波未写入快照 | PSD/STFT 显式传入高通、低通和陷波设置 | 关闭／50／60 Hz 配合 30／50 Hz 高切的快照测试全部通过；真实 PSD 关闭／60 Hz 复验通过 |

新公共函数通过关键字参数表达真实滤波，模块没有新增中央服务算法分支。RBP 保留自身固定 1–50 Hz、50 Hz 陷波；FAA 不受公共函数改动影响。

PSD、STFT、峰频率、频段功率比更新实现标识以隔离旧缓存，科学版本不变。原始审计 JSON SHA-256 仍为 `0b6e53a88f4c489209a562fa8fcad82593c6d4016556e12d87df0a7b0d905b89`，没有覆盖旧发现。

## 真实数据复验

来源目录为用户提供的本次记录，报告不保存姓名或 EEG 原始数组。来源 SHA-256：`1c3a9ef23771d6b768ec31bd9f072d9504d32d4149fe799dbd8e976f27465983`；149.982 秒，1000 Hz，149982 样本。独立读取与生产读取仍逐样本一致，0 丢样，0 非有限值。

E-001：[修复后的独立数值审计](2026-10-08_recording-algorithm-audit-fixed.json)，`fixed_contract_checks_passed=true`。9 个算法、32 组静态／动态配置，407456 个结果项、730803 个服务频谱/坐标项、4698 个质量项及6376个时间项比较失败均为 0。所有已保存的默认静态标量与修复前完全相同。对比项不是独立统计样本；频点、NaN 掩码和布尔项存在重复，不能解释为同数量的独立科学验证。

E-002：同一 JSON 的 5 个专项探测。前 3 个越界请求显式拒绝；后 2 个 PSD 陷波关闭／60 Hz 实际值和执行快照一致。正常 PSD 的参照误差门槛不变，没有放宽容差。

E-003：`validate_run_execution.py` 重跑实际记录，全部 18 个 Run 完成、18 个产物校验通过、9 个算法窗口级取消通过；FAA 130 个动态窗口一致，最大绝对误差 `3.55e-15`。

E-004：后端全量测试 434 通过，其中新增频率契约回归 58 项。2 个第三方弃用警告不影响通过。旧 PSD/STFT 测试仅同步实现标识断言，未削弱数值和轴断言。

E-005：OpenSpec `fix-algorithm-frequency-contracts` 严格验证、文件规模检查、`git diff --check` 通过。新源码和测试未超过 400 行。

P-001：配置校验 → 客户端安全参数 schema → 模块实际滤波 snapshot → queued Run 持久化 → static/dynamic 计算 → completed Run/产物 → 独立参照与边界探测。F-001/F-002/F-003 的已复验行为由 E-001/E-002/E-004 支持。

## 复现

从 `backend` 目录执行：

```powershell
$recordingDirectory = Read-Host '输入本次记录目录的完整路径'
$env:PYTHONPATH = '.'
uv run --frozen --extra dev python -m scripts.audit_recording_algorithms $recordingDirectory --expect-fixed --output ../docs/validation/2026-10-08_recording-algorithm-audit-fixed.json
uv run --frozen --extra dev python scripts/validate_run_execution.py $recordingDirectory
uv run --frozen --extra dev pytest -q
```

从仓库根目录执行 `openspec validate fix-algorithm-frequency-contracts --strict`。

## 验收边界

本次已经验证后端支持域、数值及快照，不等于 WPF 实际点击/渲染验收，也不是临床有效性认证。客户端需刷新后端算法目录才能获取新参数边界；服务端始终独立校验，不依赖客户端是否限制输入。

后续若需要 30 Hz 以上的峰频率或功率比，应另建科学契约，明确滤波范围、陷波、频率输出范围、质量门及版本，不能仅撤销本次边界校验。原审计关于 IAPF 简化模型、短窗口稳定性、预处理后质量门及非临床用途的限制仍然存在。
