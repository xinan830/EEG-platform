# PSD/RBP 回放延迟诊断埋点

日期：2026-11-06。范围：补足现有自定义频率回放的延迟证据，并修复已定位的窗口基础设施开销；不改变科学路线。

本次只改变内部生命周期和读取开销，不改变用户功能、API/WebSocket 响应、科学身份、窗口时间/步长、单位或通道顺序。PSD/RBP 仍沿用已有计算和结果投影。没有新增分析请求、依赖、数据库迁移、操作系统设置或原始录音修改。

触及的所有者：后端窗口准入/执行、公共数据库连接计数、导联快照目录、RBP 帧计时、窗口/投影 API；桌面 HTTP 流式读取、算法队列与观察生命周期、Review 结果绑定/应用、数值通知与现有 PSD 曲线更新。通用计时聚合与流读取计数各自放在诊断文件中；后端准入服务的文件规模评估已更新，保留既定身份/上下文拆分边界。

现有证据缺口是：HTTP 日志止于响应头；ready 到订阅释放、UI 续接和投影发布之间无法细分。此次分别记录这些阶段，并补充窗口实际范围、播放位置、有符号落后时间和响应体积。完整字段、关联方法和测量局限见 [采集协议](../review-scheduler-performance-protocol.md)。

## 本轮基础设施修复

真实日志的 49 次配对更新中，完整周期中位数 452.6 ms、最大 1634.9 ms，窗口末端最大落后 1.640 s。最慢 RBP 窗口的频带公式仅 4.596 ms，而取消检查累计 179.460 ms；一次准入读取 30 条快照并建立 42 个数据库连接。修复保持每次导联目录和租约状态的完整校验，不以静态本地缓存绕过身份或取消语义。

- `AnalysisMontageSnapshots` 现在批量读取快照行，并按录音、规范化目录、manifest 内容摘要和完整快照行内容做 8 项有界复用；返回深拷贝，manifest、数据库行或目录变化会失效。
- 动态窗口的元数据、导联映射、科学身份和窗口规划移到共享准入锁外；锁内仍负责窗口/attempt/subscription 的原子容量、重复请求、关闭和恢复 fencing。
- 执行 worker 使用一个独占的 autocommit 状态读取连接，以一次联接查询同时读取窗口状态、attempt 状态、租约 token 和过期时间。每个检查仍读取最新提交，外部取消/租约替换立即可见；worker 结束时关闭连接。

合成 30 快照记录的 20 次对照：目录读取中位数从 42.865 ms 降至 2.232 ms，连接从 32 次降至 2 次；100 次取消检查从 241.003 ms 降至 15.607 ms，检查阶段不再重复创建连接（worker 启动只建立 1 个状态读取连接）。这是基础设施对照，不代表真实录音和 GPU/UI 的最终时延。

验证结果：

- 后端定向集 **31 passed**，覆盖聚合计时隔离/异常恢复、锁等待与持锁耗时、窗口服务、RBP、保存窗投影 API、PSD/RBP API 和执行控制。JUnit：`_temp/spectral-latency-instrumentation-backend.xml`。增加 worker/connection/HTTP 关联字段断言后，保存窗 API 用例额外复跑 **1 passed**。
- 桌面初始专项 **51 passed**；扩大定向集 **152 passed、2 failed**。两项失败为 `Analysis_status_notifications_do_not_republish_result_and_detach_clears_panel`（Expected 1 / Actual 0）和 `Dragging_exclusively_owns_position_then_restores_previous_playback_state(True)`（Expected false / Actual true），与 [此前已核实的基线记录](2026-11-06-review-custom-frequency-inspection.md) 同名同断言。本轮未再次构建 HEAD 隔离基线，也未把它们计为通过。
- 最后一次编译与专项 **17 passed、0 failed**，覆盖流读取计数/取消/所有权、UTF-16 字符集及 null 指标、HTTP 不捕获调用者上下文、自定义数值校验和首次窗口/慢 RBP 场景。TRX：`_temp/spectral-latency-tests/spectral-latency-final.trx`。保留既有 .NET 预览 SDK、OpenTK/SkiaSharp NU1701 警告；没有增加依赖。
- 本次改动文件规模检查通过；`on_demand_dynamic.py` 372 行，新增 `dynamic_window_planning.py` 215 行，`dynamic_execution_state.py` 43 行。全仓规模检查仍有两项此前已记录的问题：`algorithm_recording_context.py` 410 行未登记、`algorithms/psd/runner.py` 464 行超过登记上限 420；未扩大本次范围处理。`git diff --check` 通过。
- 当前 PATH 没有 `openspec` 命令，因此无法运行 strict CLI；本轮已同步 OpenSpec design/tasks/spec 文件，需在安装 CLI 的环境补跑 strict。

### 基础设施修复后的补充验证

- 最新后端定向回归 **61 passed、0 failed**，覆盖目录缓存身份与 caller isolation、ID 去重、准入容量/并发/关闭竞态、取消和租约 fencing、worker disposal、进程恢复、窗口 artifacts、自定义投影 API。剩两条 FastAPI/httpx 依赖弃用警告。
- 一次更广的 backend 回归为 **292 passed、12 failed**。其中 11 项在隔离的未修改 HEAD 源码与 storage 下复现，均是 IAPF/Theta-Beta 夹具传 5 秒动态窗，但当前算法契约要求 20/30 秒；另 1 项读取旧诊断字段的断言已更新，并在上述 61 项中通过。没有更改算法的窗口限制。
- 合成基准从 `HEAD` 读取旧目录实现，与当前实现对相同 30 快照记录作 20 次对照：目录查询中位数 **42.865 ms -> 2.232 ms**，每次连接 **32 -> 2**；100 次取消检查 **241.003 ms -> 15.607 ms**，worker 独占状态读取连接仅在开始时创建一次。它不能替代真实回放、GPU/UI 或冷源加载验收。
- 本次改动后端业务文件规模检查通过。全仓仍有两项策略基线问题：`algorithm_recording_context.py` 410 行未登记，`algorithms/psd/runner.py` 464 行超过登记上限 420。`git diff --check` 和 `compileall` 通过。没有 `openspec` CLI，strict 验证未运行。
- 修复后的代码尚未在用户环境启动，尚未做新的真实录音播放对照；OpenSpec 的真实录音人工验收 4.2 仍未完成。

未重启用户正在运行的应用或服务，未进行 GUI 实录复测。更新代码需要由更新后的后端/桌面进程加载；旧进程和旧日志不会自动获得修复。使用更新后的桌面进程时，启动前设置 `BRAIN_REVIEW_PERF_DIAGNOSTICS=1`；后端使用现有 INFO 日志级别。

迁移：无。回滚：撤回本次诊断埋点即可，不需要迁移或清理科学产物。风险：日志与计时有开销；流读取诊断复用原 JSON 读取器与字符集处理，只计数而不缓存完整响应。接口回归需验证取消、不可用值、科学窗口身份及响应格式不变。不会根据这次自动化通过声称实际卡顿已修复。
