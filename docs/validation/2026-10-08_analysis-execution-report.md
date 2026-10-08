# 分析执行优化验证记录

本轮六项优化已实现并完成自动化验证；当前真实桌面上的按钮、进度变化和播放保持仍需人工验收。未提交、未推送。

FAA 已升级到 `official-faa-v2` / `faa-runtime-v2` / Definition `2.0.0`。这不是纯性能更新：连续滤波边界改变了部分数值和有效 Epoch 数。历史 v1 Run、版本和 artifact 不重算、不重命名。

## 六项结果

| 项目 | 实现边界 | 验证证据 |
| --- | --- | --- |
| 动态进度 | Run 持久化已处理/总窗口数；质量拒绝也计入已处理；WPF 运行中最多显示 99%，保存完成后 100% | 后端窗口计数、缓存复用；WPF 公共上下文百分比与通知测试 |
| 取消 | 官方算法窗口、连续预处理块之间检查；保存期间取消与完成通过事务仲裁 | 九个真实动态算法均在第一窗口后取消，无 artifact；另测压缩保存期取消 |
| 防重复 | SQLite 写事务合并同记录/项目/批次/执行身份的相同活跃配置；明确请求键保留原 Run | 并发提交、别名请求键、配置冲突、终态重跑、重复重试；WPF 自动/按钮竞争 |
| 队列隔离 | 静态交互与动态/批处理各一个 worker；池生命周期统一限制原生数值线程为 1 | 队列分区认领、背景阻塞时交互完成、重复启动、延迟退出恢复线程限制 |
| 长记录读取 | WPF 样本分块；EDF/BDF 非 preload 读取；连续滤波使用磁盘映射和跨块状态 | 65536 样本读取上界、通道次序、计数器间隙、错误/取消清理、LRU 租约与 SciPy 数值一致性 |
| FAA 契约 | 整条不可变记录的所选双通道连续滤波，再切静态/动态窗口及成对 Epoch | 独立 FFT/积分复算、静态/动态一致、旧新边界差异、历史 v1 读取 |

进度是后端执行进度，不是动态播放时间。重复提交不创建另一个 Run；重复重试命中既有活跃 Run 时，保留该 Run 最初的 parent 链。

## 真实记录

源身份 SHA-256：`90a38e2c97d3c8e9a5c739600ef8fd2c781b2502ffa0a247daf9755916e89b25`。

重新检查用户指定目录中的实际文件：时长 **63.318 s**，采样率 **1000 Hz**，注册通道表 **28 列**。此前上下文中的 149.982 s / 21 通道不适用于本次磁盘文件，不能沿用该数字。

在临时数据库及 artifact 目录中执行，不修改运行中的应用数据库、不修改原始 EEG。默认分析来源 F3；FAA 明确指定 F3/F4，Brainbeat 明确指定 Fz/Pz。动态窗口 5 s / 步长 1 s；FAA 按其现有科学策略使用 20 s / 1 s。PSD/STFT/FAA 指定高通 1 Hz、低通 50 Hz、陷波 50 Hz。

| 算法 | 静态耗时 s | 动态耗时 s | 动态已处理/总窗口 |
| --- | ---: | ---: | ---: |
| Band Ratio | 2.2777 | 1.1867 | 60/60 |
| Brainbeat | 0.0849 | 1.1263 | 60/60 |
| FAA v2 | 1.9643 | 2.7979 | 44/44 |
| IAPF | 0.0981 | 1.3092 | 60/60 |
| Peak Frequency | 0.0988 | 1.3160 | 60/60 |
| PSD | 2.6513 | 1.2245 | 60/60 |
| RBP | 0.0903 | 1.1887 | 60/60 |
| STFT | 0.1164 | 1.1152 | 60/60 |
| Theta/Beta | 0.0837 | 1.1920 | 60/60 |

18 次执行均 `completed`，窗口计数单调且最终一致，18 份 NPZ 均通过存储层 SHA-256 校验和读取。`completed` 表示执行结束，不保证每个科学窗口可用：Brainbeat 有 2 个 Rejected / 58 个 Complete，仍保留拒绝原因；支持 warm-up 的标量算法保留 Partial。

另以不同分析终点避免缓存复用，九个动态算法在已完成第一个窗口后收到取消，全部为 `cancelled`，保留 1/总窗口计数，无结果 artifact。

耗时是一次本机工程测量，包含磁盘/预处理缓存热度差异和同时运行测试的影响，不是跨机器性能保证。

## FAA 数值边界

独立参考从原文件另行读取 F3/F4，在完整数组上调用 SciPy 四阶 Butterworth、50 Hz notch；独立实现成对 Epoch 质量检查、Hann/rFFT 单边密度、8–13 Hz 梯形积分和功率对数差，不把平台最终结果作为输入。

- 44/44 窗口数值与可用性匹配，逐窗口 clean Epoch 数匹配。
- 独立容差：`rtol=1e-11, atol=1e-12`。
- 最大独立绝对误差：`3.552713678800501e-15`。
- 与旧“每个窗口单独滤波”最大绝对差异：`0.1258127160824678`。
- 43 个窗口的 clean Epoch 数或可用性与旧实现不同。这个计数不等于 43 个结果被拒绝；新实现的 44 个 FAA 窗口均为 Complete。
- 独立参考曾误用五阶滤波，修为契约四阶后通过；没有放宽容差来消除失败。

分块滤波另与完整数组 SciPy 比较：block size 17 / 997 / 65536，notch 关闭 / 50 / 60 Hz，`rtol=1e-12, atol=1e-15`。带 NaN 的输出与完整参考一致，不补零、不插值。

这些证据是工程数值验证，不是临床有效性或实时因果滤波验证。零相位整记录预处理会利用后续样本，不能直接搬入未来的在线采集计算。

## 自动化与复现

从项目根目录执行：

```powershell
Push-Location backend
pytest -q
$recordingDirectory = Read-Host '输入本地已完成 WPF 记录目录'
python -m scripts.validate_run_execution $recordingDirectory
Pop-Location
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --artifacts-path "$env:TEMP/brain-run-execution-build"
openspec validate harden-analysis-run-execution --strict
openspec validate align-faa-continuous-preprocessing --strict
python backend/scripts/check_file_sizes.py
git diff --check
```

结果：后端 **359 passed**；WPF **381 passed**，构建成功；两个 change strict 校验、文件规模策略及 `git diff --check` 通过。保留第三方 OpenTK/SkiaSharp 对 net10.0-windows 的 NU1701 兼容性警告，未更换图表依赖。

三个先前 WPF 失败测试已纠正验证前提，不靠删除断言通过：质量卡本来就始终可见，测试同时检查“尚未运行”；Brainbeat 测试先发布 LastRun，再验证 5 s 拒绝数为 0、6 s 为 1；FAA 能力检查保留且预期陷波可用。

证据与代码路径：

| 证据 | 结论 | 路径 |
| --- | --- | --- |
| 窗口/取消/并发测试 | 只计已处理窗口；取消优先；请求合并原子性 | `backend/tests/test_run_controls.py`、`app/services/run_queue.py`、`app/persistence/repositories/run.py` |
| 分块/缓存测试 | 滤波边界数值不变；正在使用的映射不会被 LRU 关闭 | `backend/tests/test_block_preprocessing.py`、`app/scientific/primitives/block_filter.py` |
| 迁移/历史测试 | v10/v11 升级不改旧结果；v1 artifact 可读取 | `backend/tests/test_execution_migrations.py` |
| FAA 独立测试/实测脚本 | 新科学身份必要且可复现 | `backend/tests/test_faa_continuous.py`、`backend/scripts/validate_run_execution.py` |
| WPF 控制/HTTP测试 | 百分比通知、取消路由、防重复、旧参数结果不覆盖 | `desktop-client/BrainPlatform.Desktop.Tests/Algorithms/AnalysisRunExecutionTests.cs` |

## 剩余验收与成本

- 重新启动后端和新编译的 WPF，确认实际详情页显示已处理/总窗口和百分比；保存结束才显示 100%。
- 对足够长的动态任务点击取消，确认不显示“取消成功”却继续发布结果；再运行可以创建新尝试。
- 在运行中切通道/参数，旧 Run 完成后不得作为新配置的结果展示；随后自动分析新配置。
- 播放进度、拖拽、暂停继续属于本地展示状态，此次没有改动图表、采集或回溯流程；仍需人工确认实际页面交互。
- 本轮没有数小时真实记录的 RSS/磁盘容量压力实测；65536 样本分块与映射所有权已验证。128 MiB 是普通数组缓存预算，不是进程 RSS 硬上限。结果矩阵、系统页缓存和临时磁盘空间仍随数据规模增长。
- 静态交互 Run 与后台动态/批处理可并行；同一后台 lane 的多个动态任务仍串行。取消等待当前 native 调用或 artifact 压缩结束，不是强杀线程。
