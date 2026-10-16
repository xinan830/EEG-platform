# 全量与按需分析 WPF 人工验收记录

记录日期：2026-10-15。范围：算法列表及详情页中的 `full` / `on_demand` 两种离线执行方式；实时分析不在本轮范围内。

## 结论与证据边界

用户在本轮对话中逐项测试并确认九个算法可用，正常使用路径按用户反馈验收通过。随后在 Review 综合场景与后端停止/恢复验收指引后反馈“可以正常”，据此登记综合验收通过。该结论来自人工反馈，不是本轮代理操作 GUI 或重新执行数值对照得到的结论；未新增逐操作日志或性能测量。

本报告保留人工反馈及最初文档同步的证据边界。用户随后要求免命令切换，默认开放的行为变更独立记录于 `enable-accepted-offline-analysis-selection`；不改变科学契约或迁移数据。数值正确性仍以各算法已有对照报告为依据，不用图形相似或缓存命中替代独立计算证据。

## 人工反馈来源

E-GUI：来源为本轮用户在对应验收指引之后的回复，记录时间为本报告日期。原始反馈保留在对话中；本轮未新增截图、逐项操作日志、Run/session ID 或性能采样，不能反推每个负例都已执行。

| 算法 | 开关后缀 | 用户回复摘录 | 本轮记录 |
| --- | --- | --- | --- |
| PSD | `PSD` | “我验证完了psd算法没有问题” | 用户确认通过 |
| STFT | `STFT` | “ok测试了”，随后继续下一项 | 用户报告测试完成，未报告异常 |
| 相对频段功率 | `RBP` | “验证完了我们继续” | 用户确认继续，未报告异常 |
| 频段功率比 | `BAND_RATIO` | “可以了继续” | 用户确认通过 |
| 频段峰频率 | `PEAK_FREQUENCY` | “ok可以了” | 用户确认通过 |
| 个体 Alpha 峰频率 | `IAPF` | “ok测试完了” | 用户报告测试完成，未报告异常 |
| Theta/Beta 比值 | `THETA_BETA` | “也可以了” | 用户确认通过 |
| 额叶 Alpha 不对称性 | `FAA` | “可以了” | 用户确认通过 |
| 脑节律指标 | `BRAINBEAT` | “ok了” | 用户确认通过 |

F-GUI：依据 E-GUI，本轮逐算法正常路径可以登记为用户验收通过；缺少独立故障注入或逐项计时证据的内容，继续列为待验证。

## 当时的复现路径与当前入口

P-GUI：关闭旧前后端，从项目根目录运行源码启动脚本，选择同一份已完成 Recording，再对同一算法依次测试全量、按需和模式往返。前述指引关注同一实际窗口的值、单位、来源、质量，以及跳转后旧结果不得覆盖最新目标；它不要求拖动每一帧都重新计算。

以 Brainbeat 为例，在项目根目录 PowerShell 运行：

```powershell
$env:BRAIN_ENABLE_ON_DEMAND_BRAINBEAT="1"
$env:BRAIN_WPF_ON_DEMAND_BRAINBEAT="1"
.\start_brain_desktop.bat
```

以上是本轮逐算法验收时的启用方式。后续免命令改动将九个已验收模块默认开放，当前只需双击 `start_brain_desktop.bat`，在“算法列表 → 动态 → 分析方式”选择全量或按需；无需输入以上命令。环境变量仍可用于维护覆盖。

当前工作树基于 `main` 的 `190472c`，含未提交实现；该提交号仅标识基线，不是本轮全部受测源码的不可变版本。复验须保留对应源码和参数快照。真实 EEG 与个人路径不复制到本报告中。

## 算法特有的核对点

- PSD、STFT：实际窗口、频率轴、功率单位和时频坐标对应正确。
- RBP、频段功率比：区分相对占比与无量纲比值，分子/分母频段按请求生效。
- 频段峰频率、IAPF、Theta/Beta：区分离散频率网格选峰、Peak/COG 来源和 IAPF 个体化频段。
- FAA：使用真实 F3/F4 来源，按其模块声明的窗口与干净 Epoch 要求验收。
- Brainbeat：使用真实 Fz/Pz 来源，IAPF 或质量不满足时说明原因。

以上是已下发的核对要求。用户给出整体反馈，不据此新增“已强制触发所有不可用窗口”的声明。

## 后续综合验收反馈

1. 在数据回溯工作区单独检查倍速播放、切换 Recording、关闭与重开分析面板，以及这些操作之后波形/游标和结果的对应关系。算法列表验收不自动覆盖 Review。
2. 只在离线回溯、没有采集或其他任务时，手动停止用于本轮验收的后端。检查波形仍可播放和拖动，算法显示明确错误而不冒用旧结果；恢复后端后能重新请求。不要用采集过程做此故障测试。
3. 对同窗去重、订阅隔离、队列有界性、缓存命中和旧请求晚返回继续使用自动化证据；本轮没有 GUI 计数或延迟分布，不新增帧率与延迟保证。

用户在上述指引后反馈“可以正常”，据此勾选 `on-demand-dynamic-analysis` 阶段 6 最终验收及对应 Review/后端故障项。该反馈没有逐项操作日志，不扩展为未执行负例或帧率保证。PSD/STFT 已声明跨模式窗口复用；其余算法的双模式 GUI 通过不代表已开放跨模式复用。全量结果尚不反向填充按需存储。

## 关联证据与维护范围

- [实施任务](../../openspec/changes/on-demand-dynamic-analysis/tasks.md)：保留未验证项。
- [算法能力矩阵](../algorithms/on-demand-dynamic-capability-matrix.md)：科学边界和当前状态。
- [2026-10-14 自动化报告](2026-10-14-on-demand-dynamic-analysis-final.md)：历史测试与构建证据，不改写其日期或数字。
- [PSD 共享身份任务](../../openspec/changes/share-offline-scientific-window-identity/tasks.md)、[STFT 共享身份任务](../../openspec/changes/share-stft-offline-scientific-window-identity/tasks.md)：后续自动化及跨模式复用证据。

最初仅文档同步时没有重跑后端/WPF 全量测试。后续默认开放改动的回归、构建和验证边界独立记录于
[下拉选择验证](2026-10-15-offline-analysis-dropdown.md)，不把自动化回归写成人工数值或性能结论。

本次文档检查结果：

- `openspec validate on-demand-dynamic-analysis --strict`：通过。
- `openspec validate share-offline-scientific-window-identity --strict`：通过。
- `openspec validate share-stft-offline-scientific-window-identity --strict`：通过。
- `backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py`：通过，保留四个已登记的历史超 400 行文件提示。
- `git diff --check`：通过。
