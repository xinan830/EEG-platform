# Brainbeat 按需动态分析阶段 4 验证报告

日期：2026-10-14

范围：Brainbeat 按需单窗 capability、真实 Recording/API 对照、WPF 详情页解析与 provenance 校验。
结论类型：工程与科学契约等价验证，不是临床有效性结论，也不是最终 GUI 验收。

## 结论

Brainbeat 已完成阶段 4 的实现和自动化验证，生产开关仍关闭：

- 后端通过独立 `BrainbeatWindowCapability` 暴露按需窗口，不复用单通道标量适配器。
- 每个窗口显式读取 Fz/Pz 两个来源，使用 IAPF 派生依赖计算 Fz 相对 Theta / Pz 相对 Alpha。
- 窗口之间不共享 EMA 或上一窗口数值；provenance 明确 `independent_windows=true`、`cross_window_ema=false` 和 `iapf_dependency=true`。
- 质量门失败返回 `rejected`、结构化详情和 `null/NaN` 科学值，不改成 0。
- WPF 只解析后端返回的 ratio、时间轴、质量、来源角色、IAPF evidence 和 provenance，不重新计算 Brainbeat 或 IAPF。
- `BRAIN_ENABLE_ON_DEMAND_BRAINBEAT` 与 `BRAIN_WPF_ON_DEMAND_BRAINBEAT` 默认均为关闭。
- 阶段 4 的人工 GUI 验收尚未完成，因此不能据此宣称按需动态功能整体完成，也不会直接进入 STFT。

## 自动化证据

### E-001：Brainbeat 后端专项

复现命令：

```powershell
backend\.venv\Scripts\python.exe -m pytest -q backend/tests/test_brainbeat_algorithm.py backend/tests/test_brainbeat_on_demand_capability.py backend/tests/test_on_demand_registry.py backend/tests/test_on_demand_output_schema.py
```

结果：`17 passed`。覆盖静态/完整动态兼容、Fz/Pz 顺序、真实 Recording/API 逐窗对照、首窗最小窗口、质量拒绝、取消、schema、能力注册和来源文件 hash 不变。

真实格式 API 对照使用脱敏合成 RecordingService 文件，完整动态 Run 与每一个按需窗口使用相同源、配置、窗口边界和算法身份；比较值、实际范围、ratio 单位、Fz/Pz evidence、IAPF evidence 及 provenance。

### E-002：WPF Brainbeat 专项

复现命令：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore --filter "BrainbeatDetailTests|BrainbeatOnDemandDetailTests"
```

结果：`6 passed`。覆盖：

- ready 窗口值和 Fz/Pz 标签绑定；
- 详情页在窗口结果先到、页面后创建时读取已有结果；
- quality failure 与 null 语义；
- 缺少独立窗口 provenance 时拒绝；
- 来源角色不匹配或 IAPF 依赖未声明时拒绝；
- 动态结果生命周期与已有完整 Run 回退路径。

Release 测试保留现有 `NU1701` 包兼容警告；本阶段没有新增编译错误。

## 科学契约检查

| 检查项 | 判定 |
| --- | --- |
| 来源 | 必须是显式 Fz + Pz，不能按通道顺序猜角色 |
| 计算 | IAPF 驱动的 Fz 相对 Theta / Pz 相对 Alpha |
| 窗口 | 后端依据游标规划半开样本范围；不足返回 `insufficient_data` |
| 窗口独立性 | 每窗独立加载/计算，无跨窗 EMA |
| 质量 | 质量拒绝与不可用保留结构化原因，科学值不伪造为 0 |
| 单位和形状 | `scalar_series`，`time_s=[end_s]`，`brainbeat=[value]`，单位 `ratio` |
| 前端边界 | WPF 仅解析、校验和展示，不重算 Brainbeat/IAPF |

## 未完成与风险

1. 需要用户在实际 WPF 记录上验收动态播放、暂停、拖动、首窗不足、质量失败详情和关闭开关后的完整 Run 回退。
2. 生产开关默认关闭，未通过 GUI 验收前不会改变用户现有路径。
3. 本报告证明实现与现有算法契约的工程等价，不证明临床阈值或医学解释有效性。
4. STFT 尚未迁移，保持旧完整 Run 路径。

## 阶段门

自动化阶段 4 已完成，当前停在“用户阶段 4 GUI 验收”。验收通过后再更新任务状态并进入下一阶段；在此之前不打开 Brainbeat 生产开关。
