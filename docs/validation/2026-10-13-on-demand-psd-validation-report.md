# 按需 PSD 验证与资源报告（2026-10-13）

## 结论与边界

PSD 的后端按需单窗路径、详情/Review 适配和自动化验证已具备证据；真实记录的
114 个窗口与同配置完整 Run 数值、频率轴和窗口 evidence 完全一致。
这不是全部动态算法迁移完成，也不是 WPF GUI 流畅性验收。
生产开关仍默认关闭。2026-10-13 用户已确认调整顺序：PSD 自动化门通过后继续
P4 逐算法迁移，GUI 留到最后统一验收；不因此宣称整体已验收。

含 gap 的小时合成源返回 53 个 rejected、没有 READY artifact。当前连续双向滤波
沿用旧路径的 NaN 传播行为，不能将“完整路径等价”解释为“gap 两侧都有有效结果”。
若改变分段滤波规则，必须另建科学契约变更和数值对照，不能作为本次性能补丁。

范围来自 [OpenSpec 设计](../../openspec/changes/on-demand-dynamic-analysis/design.md)。
仅 PSD 声明按需能力，其他 8 种算法维持旧完整 Run。原始文件只读，全部诊断数据库、
完整 Run 对照产物和新窗口产物在临时目录创建，测试结束清理，不写入真实源目录。

## 环境与证据

Windows 11 build 26300；Python 3.13.3、NumPy 2.5.3、SciPy 1.18.1；.NET SDK 10.0.401。
分支 `feature/on-demand-dynamic-analysis`，基础提交
`45714cc814496455a2f3d0bc2ecdac50501bfccb`，验证包含尚未提交的实现。
脱敏测量摘要见 [measurements.json](2026-10-13-on-demand-psd-measurements.json)。
不保存姓名、原始 EEG 数值、真实目录和真实 recording ID。

| 证据 | 可复现入口 | 观察 |
| --- | --- | --- |
| E-001 | `backend/scripts/validate_on_demand_psd.py --recording` | 60.623 秒、2000 Hz、前 2 个采集导联，114/114 READY，最大绝对/相对误差均为 0 |
| E-002 | 同脚本的 short/continuous/gap | 60 秒及 3600 秒、200 Hz、2 通道，顺序/随机目标和 4 消费者并发；耗时与资源如下 |
| E-003 | `tests/test_on_demand_process_recovery.py` | 真正终止 worker OS 进程，新进程不依赖新 ensure 自主恢复；源身份变化明确失败 |
| E-004 | `uv run pytest -q` | 后端全量 604 passed，2 个第三方弃用警告，94.44 秒 |
| E-005 | WPF 测试项目及 `ReviewOnDemandProgressRaceTests.cs` | 全量 611 passed；竞态专项 3 passed，延迟进度/旧结果不能覆盖新目标或重开配置 |
| E-006 | strict/build/file-size/diff 检查 | OpenSpec valid；最后一轮 Release 0 errors、6 个既有 NU1701；规模策略通过；差异无空白错误 |

E-001 的原始文件哈希及两份对照完整 Run NPZ 哈希保存在 JSON 中。
`source_and_history_unchanged` 指诊断读取的源文件和本次创建后保持不可变的完整 Run
对照产物；没有遍历用户其他项目的历史库。旧 Run/API/artifact 回归由 E-004 单独覆盖。
诊断期间生成的 Definition ID 是临时库身份，不能将下次运行的新 ID 当作数值变化。

## 科学对照

两条路径使用相同源 bytes、采集导联来源、通道、采样率、配置、窗口计划和科学实现。
保持 `offline-spectral-v3` / `psd-runtime-v3-explicit-filters`，不修改公式或版本。
首期先整段连续 zero-phase 预处理，再取单窗，不能截短局部后重新滤波。

| 对照项 | 判定 | 实测 |
| --- | --- | --- |
| PSD 与 band share | `rtol=1e-12`；PSD `atol=1e-24`；ratio `atol=1e-12` | 114 窗口，失败窗口 0，最大绝对/相对误差 0 |
| 频率轴 | 逐点相等 | 相等 |
| 坐标 | 相同半开 start/end sample | 相等；以 recording-relative sample 为准 |
| 单位/质量 | PSD `V^2/Hz`，ratio；旧完整窗对应状态 | READY 114；窗口完整 evidence 相等 |
| provenance | 连续预处理、source/config/Definition/实现/policy 身份 | `whole_recording_before_window_slice`，身份摘要保存在 JSON |

fixture 专项补充覆盖非零首计数器、首窗/边界/尾窗、质量拒绝、gap、manifest 异常、
缺少历史导联快照、无效单位和源/配置/版本变化。通用 output schema 检查轴单调性、
rank、共享维度长度、band share 形状、单位、finite 和 output kind；无效输出不发布 READY。
这属于实现等价验证，不是独立证明 PSD 科学公式或真实 EEG 的临床有效性。

## 性能与资源

单位为 ms，内存/文件使用 MiB（1048576 bytes）；精确 bytes 保存在 JSON。
“冷首窗”不含前面的注册时间，但包含源准备、排队、计算、发布、轮询和订阅释放。
冷注册、冷首窗和首次连续预处理各只有 1 次测量，不能宣称其 p95。
“后窗”是 20 个顺序目标和 24 个随机目标的混合，可能包含缓存命中；并非纯命中率实验。

| 指标 | 60 秒连续 | 3600 秒连续 | 3600 秒 gap |
| --- | ---: | ---: | ---: |
| 冷/热注册 | 23.518 / 8.167 | 29.277 / 17.876 | 34.119 / 17.845 |
| 冷首窗（n=1） | 191.037 | 383.005 | 377.107 |
| 后窗 p50 / p95（n=44） | 129.562 / 164.388 | 139.658 / 173.207 | 112.125 / 143.567 |
| 并发 p50 / p95（n=8） | 547.462 / 696.369 | 569.821 / 861.461 | 531.051 / 711.290 |
| 连续预处理（n=1） | 16.710 | 155.225 | 161.646 |
| 单窗计算 p50 / p95 | 10.285 / 26.824 | 9.686 / 25.222 | 4.811 / 20.447 |
| 发布及验证 p50 / p95 | 15.086 / 19.834 | 16.029 / 21.559 | 无发布 |
| HTTP 请求 | 246 | 294 | 239 |
| 目标终态 | READY 53 | READY 53 | rejected 53 |
| 整段诊断总耗时 | 6964.681 | 8146.140 | 6770.965 |
| CPU 秒 | 5.813 | 6.797 | 5.438 |
| RSS 基线 / 采样峰值 MiB | 156.97 / 157.41 | 161.74 / 202.63 | 174.79 / 218.89 |
| 临时映射文件峰值 MiB | 0.734 | 43.947 | 43.947 |
| 派生 artifact bytes | 339424 | 390748 | 0 |

JSON 另含 source-read block、准备/缓存解析、artifact read 分布和最大耗时。
每场景 53 个目标是请求终态计数，不等于 53 个唯一窗口，缓存命中会减少计算/发布次数。
资源采样覆盖实际 map 分配/释放，不只统计缓存留下的文件。

测量限制：同机 FastAPI TestClient、诊断 20 ms 轮询；不是实际网络或 WPF 帧时间。
WPF 实际观察间隔是 200 ms，还会叠加网络、Dispatcher 和图表绘制。
RSS 每 20 ms 采样可能漏掉短峰值，且包含进程/页缓存的影响；临时 map 指逻辑文件容量，
不是全进程 RAM 上限。小时压力只有 2 通道、200 Hz，不代表 32 通道、2000 Hz。
诊断等待 deadline 180 秒只用于测试失败退出，不是生产分析时长上限。

## 结论与调用链

- F-001（E-001、E-004，已验证）：PSD 单窗在本报告同源/同配置范围内与旧完整 Run 等价；不修改科学版本。
- F-002（E-002、E-004，范围内已验证）：单 worker、全局/项目/recording 配额及 map/存储检查可有界拒绝新工作；不是所有负载下的 RSS 保证。
- F-003（E-003、E-004，已验证）：真实 OS 死亡后可自主恢复未发布窗口，旧 token 不能发布；测试在死亡后主动过期 disposable DB lease，不证明生产恢复延迟。
- F-004（E-002、旧 `test_gap_propagation_matches_full_reference`，已知限制）：连续滤波 NaN 传播导致含 gap 场景全部拒绝；保留旧科学结果，不伪造成功。

P-001（callflow）：只读 WPF 文件注册（E-001）→ 全段连续准备 → 后端规划样本窗 →
单窗算法 → schema/checksum/fencing 原子发布 → WPF 类型化读取/单位投影（E-004、E-005）。
剩余风险是高负载首准备延迟、真实网络与 GUI、gap 有效区间科学契约，以及其他算法独立迁移。

首期默认预算见 design：单 worker，全局 16 / 项目域 8 / recording 4 个活动 attempt；
无预取；map 8 GiB、磁盘保留 1 GiB、窗口目录 2 GiB。资源不足明确失败/429，
不截断原始记录，不删除 READY，不把资源失败标为科学质量拒绝。

## 复现与下一步

从 `backend` 执行合成诊断和后端回归：

```powershell
uv run --with psutil python scripts/validate_on_demand_psd.py --duration-s 3600
uv run pytest -q
```

真实对照需要受授权的原始记录目录；为避免提交个人路径，复现者在控制台输入路径：

```powershell
$validationSource = Read-Host '请输入已完成录制目录的绝对路径'
uv run --with psutil python scripts/validate_on_demand_psd.py --recording $validationSource
```

从仓库根目录执行桌面和工程检查，独立产物目录不占用正在运行的 Debug 客户端：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --artifacts-path "$env:TEMP/brain-on-demand-test-build" --logger 'trx;LogFileName=on-demand-final.trx' --results-directory "$env:TEMP/brain-on-demand-test-results"
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --artifacts-path "$env:TEMP/brain-on-demand-release"
openspec validate on-demand-dynamic-analysis --strict --no-interactive
git diff --check
```

统一交付验收仍包括真正 GUI 播放/暂停/倍速/拖动/关页/后端不可用时波形独立可用。
此前继续逐算法自动化迁移，但不默认打开生产开关；STFT、RBP、FAA 等须各自完成
科学/模块验证，不能从本 PSD 报告推断它们已迁移。
