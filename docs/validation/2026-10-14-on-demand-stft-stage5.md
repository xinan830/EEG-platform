# STFT 按需动态分析阶段 5 验证报告

日期：2026-10-14

范围：STFT 按需单窗 capability、时频矩阵/轴契约、质量状态、WPF 详情页解析。
结论类型：工程与科学契约等价验证，不是临床有效性结论，也不是最终 GUI 验收。

## 结论

STFT 已完成阶段 5 自动化实现，生产开关仍关闭：

- 后端新增 `StftWindowCapability`，复用 `spectrogram-v2` 与连续预处理后的既有 loader；通用窗口服务不包含 STFT 分支。
- 后端按游标规划外层动态窗口；4 秒内部 STFT segment、1 秒 step 保持不变。
- 按需 READY 结果只包含 loader 实际产生的有限时频行；首个最小窗口不会用 NaN 填充成假矩阵。
- 结果明确声明 `time_center_s`、`frequency_hz`、`power_linear` 和 `power_db` 的单位与 shape；时频中心轴为 recording 绝对秒坐标。
- 质量门失败返回 `rejected` 与结构化 failure/evidence，不发布非有限 ready 数组；数据不足返回 `insufficient_data`。
- WPF 只解析后端时频轴、矩阵、单位和 provenance，不重新计算 STFT、滤波或质量。
- `BRAIN_ENABLE_ON_DEMAND_STFT` 与 `BRAIN_WPF_ON_DEMAND_STFT` 默认均为关闭。

## 自动化证据

后端复现命令：

```powershell
backend\.venv\Scripts\python.exe -m pytest -q backend/tests/test_stft_on_demand_capability.py backend/tests/test_on_demand_registry.py backend/tests/test_on_demand_output_schema.py
```

结果：`14 passed`。另有 backend 全量 `657 passed`。

覆盖：首窗/完整窗规划、真实部分矩阵、完整动态 Run 对照、频率轴一致性、质量拒绝、取消检查、单位/shape/schema 和能力注册。

WPF 复现命令：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --filter "FullyQualifiedName~StftOnDemandDetailTests"
```

结果：`3 passed`。WPF 全量 `651 passed`，Release 编译路径保留既有 NU1701 包兼容警告，未见新增编译错误。

覆盖：按需时频矩阵绑定、绝对时频中心轴、质量失败详情、错误状态无图表、错误单位/shape/provenance 拒绝，以及旧完整 Run 路径未被替换。

## 科学契约检查

| 检查项 | 判定 |
| --- | --- |
| 预处理 | `whole_recording_before_window_slice`，不在局部窗口重新滤波 |
| 内部 STFT | segment 4 s，step 1 s，时频中心时间轴 |
| 单窗输出 | `time_frequency`；轴为 `time_center_s`/`frequency_hz`，矩阵为 `inner_time x frequency` |
| 单位 | `power_linear=V^2/Hz`，`power_db=dB re 1 uV^2/Hz` |
| Partial | 仅返回实际存在的有限内部时频行，不伪造 0 或 NaN READY 矩阵 |
| 质量 | rejected 与 failure/evidence 分离；不把质量失败当作可用结果 |
| 前端边界 | WPF 只校验和投影后端数组、单位、范围和 provenance |

## 中央接入记录

| 文件 | 变更 | 原因 |
| --- | --- | --- |
| `backend/app/algorithms/stft/manifest.py` | 声明窗口输出 kind、轴和 shape | 让通用发布校验能验证 STFT 单窗契约 |
| `backend/app/algorithms/stft/runner.py` | 继承模块能力 | 模块自身提供单窗计划/执行，不增加通用 executor 分支 |
| `backend/tests/test_on_demand_registry.py` | 更新显式能力基线 | STFT 现在正式声明 capability |
| WPF STFT handler/preview/state | 增加按需窗口解析与投影 | 保持 chart 状态归属 STFT 模块，catalog 只协调请求 |

## 未完成与风险

1. 需要用户在实际 WPF 记录上验收动态 STFT 的首窗不足、拖动/播放、暂停、窗口切换、质量失败详情和关闭开关后的旧完整 Run 回退。
2. 生产开关默认关闭，自动化通过不等于已经改变现有用户路径。
3. 本报告证明实现与当前 `spectrogram-v2` 工程契约一致，不证明临床解释或阈值有效性。
