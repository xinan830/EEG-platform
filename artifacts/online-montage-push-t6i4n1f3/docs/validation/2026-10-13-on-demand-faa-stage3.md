# FAA 按需动态分析阶段 3 验证报告

日期：2026-10-13

范围：`faa` 按需单窗 capability、WPF 详情页解析与完整动态 Run 对照。
结论类型：工程与科学契约等价验证，不是临床有效性结论，也不是最终 GUI 验收。

## 结论

FAA 已完成阶段 3 的实现和自动化验证，生产开关仍关闭：

- 后端通过独立 `FaaWindowCapability` 暴露按需窗口，不复用标量适配器。
- 计算保持成对 F3/F4 来源、整段连续预处理后取窗、2 秒 epoch、1 秒步长、最少 10 个干净 epoch 和 150 µV artifact 门。
- WPF 只解析后端返回的 `faa`、单位、时间轴、质量、FAA evidence、calculation trace 和 provenance；不在客户端计算 FAA。
- `BRAIN_ENABLE_ON_DEMAND_FAA` 与 `BRAIN_WPF_ON_DEMAND_FAA` 默认均为关闭。
- 阶段 3 的人工 GUI 验收尚未完成，不能据此宣称所有算法或按需功能整体完成。

## Evidence

### 后端数值与契约

命令（纯模块/独立参考）：

```powershell
backend\.venv\Scripts\python.exe -m pytest -q backend/tests/test_faa_continuous.py backend/tests/test_faa_on_demand_capability.py backend/tests/test_on_demand_registry.py backend/tests/test_on_demand_output_schema.py
```

结果：`18 passed`。

端到端真实 RecordingService/API 命令：

```powershell
backend\.venv\Scripts\python.exe -m pytest -q backend/tests/test_faa_on_demand_capability.py
```

结果：`3 passed`，其中真实文件链路逐一对照完整动态 Run 的 `23` 个 FAA 窗口。

覆盖内容：

| 检查项 | 证据 |
| --- | --- |
| 完整动态 Run 与按需窗口 | 同一合成源、同一配置，在多个窗口边界比较 FAA 值；逐窗 evidence 与 provenance 一致 |
| 独立参考 | 连续 1–50 Hz zero-phase 预处理后，用独立 Hann/rFFT 参考计算对照静态/动态结果 |
| 来源契约 | F3/F4 成对来源，来源标签进入 evidence；相同通道被配置校验拒绝 |
| epoch 契约 | 2 秒 epoch、50% overlap、1 秒 step、Alpha 8–13 Hz、频率分辨率 0.5 Hz |
| 质量门 | 非有限样本、150 µV 超幅、少于 10 个干净 epoch 返回 null/`rejected`，不伪造 0 |
| 按需 schema | `scalar_series`、`time_s=[window.end_s]`、`faa` 单值和 `dimensionless` 单位通过后端 schema 校验 |
| 取消 | 连续预处理前的取消检查触发，窗口不会继续计算 |
| 注册边界 | registry 只暴露显式声明的 FAA capability；未声明算法不回退到完整 Run |

### WPF 解析与失败展示

命令：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore --filter FaaDetailTests
```

结果：`6 passed`；保留既有 `NU1701` 包兼容警告，无新增编译错误。

WPF 专项现在验证：

- FAA 窗口值、F3/F4 evidence 和连续预处理 provenance 可见。
- 单位、轴值、数组形状或 provenance 不匹配时拒绝绑定。
- 质量拒绝保留结构化 details，值保持 null，状态为 `Rejected`。
- 静态旧结果仍走原有完整 Run 解析路径。
- 详情页在窗口响应已到达后才创建时，会立即绑定 Catalog 中已有的窗口结果，避免生命周期竞态导致空白详情页。

## Finding

1. FAA 与 PSD 的 prepared-source 语义不能合并。FAA 的窗口 capability 仍通过 FAA 自己的 `load_faa_signals` 入口读取连续预处理源，manifest 和 provenance 单独声明 `faa-dynamic-window-v1` 与 paired epoch 契约。
2. 这轮发现并修正了 WPF 绑定的一个完整性缺口：此前 ready 窗口未强制验证 `dimensionless`、`time_s` 轴、单值形状、FAA evidence 和 `whole_recording_before_window_slice`，可能出现“请求成功但页面显示不完整”。
3. 增加了详情页生命周期回归测试：窗口结果先进入 Catalog，再创建 FAA 详情页时，值、F3/F4 来源和质量状态均立即可见。
4. 自动化测试无法证明真实机器上的 SciChart 播放、拖动、暂停和窗口切换体验，因此 GUI 验收仍保持未完成。

## Path

| 层 | 文件 |
| --- | --- |
| FAA capability | `backend/app/algorithms/faa/window.py` |
| FAA manifest/runner | `backend/app/algorithms/faa/manifest.py`、`backend/app/algorithms/faa/runner.py` |
| 连续预处理源 | `backend/app/services/algorithm_recording_context.py` |
| 后端专项测试 | `backend/tests/test_faa_on_demand_capability.py`、`backend/tests/test_faa_continuous.py` |
| WPF FAA 结果投影 | `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Faa/FaaResultPreview.cs` |
| WPF FAA 详情状态 | `desktop-client/BrainPlatform.Desktop/Modules/Algorithms/ViewModels/Faa/FaaDetailViewModel.cs` |
| WPF 专项测试 | `desktop-client/BrainPlatform.Desktop.Tests/Algorithms/FaaDetailTests.cs` |

真实端到端 fixture 只使用脱敏合成 Recording 文件；测试结束后比较 manifest、样本和审计文件
的 SHA-256，确认分析没有改写原始记录。

## 限制与下一步

- 本报告不包含真实用户 EEG 文件，不提交个人路径或原始数据。
- 当前尚未完成真实 GUI 手测：切换 FAA、播放/暂停、拖动、质量详情和后端不可用时的旧回放保持可用。
- 阶段 3 用户验收通过后，才进入 Brainbeat；在此之前 FAA 生产开关继续关闭。
