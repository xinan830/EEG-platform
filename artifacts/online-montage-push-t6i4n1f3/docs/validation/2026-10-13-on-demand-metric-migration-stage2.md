# 按需动态分析阶段 2：RBP 与标量迁移证据

日期：2026-10-13

范围：RBP、频段功率比、频段峰频率、IAPF、Theta/Beta。

生产开关：全部保持默认关闭。
结论性质：这是“完整 Run 与同一后端模块按需窗口”的迁移对照，不是独立科学参考实现。

## Evidence

### E-001：真实格式 API 逐窗口对照

- source_type: command
- source_ref: `backend/scripts/validate_on_demand_metrics.py`
- repro_command: `backend/.venv/Scripts/python.exe backend/scripts/validate_on_demand_metrics.py`
- fixture: 脱敏 WPF 格式合成记录，200 Hz、22.35 s、F3/F7-REF、首样本计数器 700
- scenarios: 连续、9–10 s gap、超幅质量拒绝、10 s 动态窗口、分析起点 2 s
- comparison: 实际 sample/time range、state、quality、unit、axis、array、failure、evidence、provenance
- tolerance: `rtol=1e-12`、`atol=1e-12`

### E-002：WPF 投影自动化对照

- source_type: command
- source_ref: `desktop-client/BrainPlatform.Desktop.Tests/Algorithms/ScalarOnDemandDetailTests.cs`
- repro_command: `dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --filter "FullyQualifiedName~ScalarOnDemandDetailTests" --no-restore -p:BaseOutputPath=.verify-scalar-stage1/`
- result: 25/25 passed
- coverage: Partial 有值、失败 null/定量原因、session/通道切换、关闭重开、旧 Run 回退、shape/unit/evidence、256 条有界历史

### E-003：RBP 回归

- source_type: command
- source_ref: `backend/tests/test_on_demand_rbp_api.py`, `desktop-client/BrainPlatform.Desktop.Tests/Algorithms/RbpOnDemandDetailTests.cs`
- repro_command: `pytest tests/test_on_demand_rbp_api.py` and the RBP WPF filter from the stage-1 verification command
- result: backend real-file scenarios passed; WPF RBP regression passed 5/5

## Finding

### F-001：五个模块的按需结果与完整 Run 对照一致

- status: validated
- evidence_ids: [E-001, E-002, E-003]
- result: 正常、10 s 和起点 2 s 场景的所有 ready 标量值/五频段值最大绝对误差和最大相对误差均为 0；对应 evidence、单位、轴和实际范围一致。
- failure behavior: gap 与超幅场景返回完整 Run 相同的质量拒绝/失败信息，HTTP 非 ready 不返回科学数组，不转换为 0。
- provenance: 各 ready 窗口保持 `whole_recording_before_window_slice`，不局部重滤波。

## 汇总

| 算法 | 单窗输出 | 单窗单位 | WPF 投影 | 自动化状态 |
| --- | --- | --- | --- | --- |
| RBP | Delta/Theta/Alpha/Beta/Gamma | `ratio` | 独立模块投影 | 通过 |
| 频段功率比 | `ratio` | `ratio` | 独立模块投影 | 通过 |
| 频段峰频率 | `peak_frequency_hz` | `Hz` | 独立模块投影 | 通过 |
| IAPF | `iapf_hz` | `Hz` | 独立模块投影 | 通过 |
| Theta/Beta | `theta_beta` | `dimensionless` | 独立模块投影 | 通过 |

## 限制与下一步

这份报告没有打开生产开关，也没有把自动化测试当作最终 GUI 验收。FAA、Brainbeat、
STFT 尚未迁移；RBP/标量的生产 GUI 交互、真实用户记录和最终总体验收仍需后续阶段完成。
