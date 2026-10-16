# 按需优先与后台全量补齐：调度验证记录

验证日期：2026-10-16。范围：共享科学计算协调器、Full 等待按需窗口时的租约交接、取消与关闭生命周期。实时分析不在本轮范围内。

## 结论

本轮修复并验证了一个真实的并发阻塞风险：Full 动态窗口在等待已有按需窗口产物时不再持有唯一 batch 计算槽。缓存等待阶段会释放 batch 租约；缓存命中后直接复用，缓存未命中才重新申请 batch 租约继续数值计算。

本轮没有实现 durable single-flight、PreparedSourceContract，也没有完成真实设备或长记录性能验收，因此不能据此宣称零延迟、固定帧率或整个 OpenSpec 已完成。

## Evidence

自动化证据：

- 后端全量：`714 passed`，命令为 `pytest -q`（工作目录 `backend`）。
- 调度专项：`5 passed`，覆盖 Full 缓存等待让出槽位、缓存未命中重新申请、取消响应、协调器关闭唤醒和交互突发后的 batch 让行。
- WPF 测试：`708 passed`，命令为 `dotnet test BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore --nologo`。
- WPF Release 构建成功，0 errors；保留 .NET 预览版和旧目标框架包兼容性警告。
- OpenSpec 全局严格校验：`88 passed, 0 failed`，命令为 `openspec validate --all --strict`。
- `git diff --check` 通过；仅报告换行符转换提示，无空白错误。
- 文件规模检查通过；现有 5 个超过 400 行的业务文件均已有策略登记，本轮新增调度文件未新增超限项。

新增调度测试文件：

- `backend/tests/test_scientific_compute_coordinator.py`

## Finding

修复前的等待顺序可能是：Full 持有唯一 batch 槽 → Full 等待按需窗口完成 → 按需窗口等待 interactive 槽 → 两条路径互相等待。现在 `RunExecutionContext.cached_window()` 在调用缓存 provider 前释放 batch 租约；provider 的持久化等待支持取消检查；未命中时重新申请 batch 槽。应用 lifespan 关闭时显式关闭共享协调器，等待中的 acquire 会收到关闭异常并退出。

仍保留的未完成项：

- 同一 `WindowKey` 的 durable single-flight、消费者所有权转移和共享取消语义；
- PSD/STFT 以外算法的 PreparedSourceContract；
- 后台最低执行份额、CPU/内存/磁盘和不可中断窗口的真实测量；
- WPF 拖拽、播放、页面重开和后台进度不污染图表的人工验收；
- 真实 Recording 上的按需优先 + Full 补齐端到端性能记录。

## Path

关键实现路径：

- `backend/app/services/run_execution_context.py`：Full 窗口租约在缓存等待与实际计算之间交接。
- `backend/app/services/offline_window_reuse.py`：按需窗口终态等待接收取消回调。
- `backend/app/persistence/repositories/dynamic_attempt.py`：持久化窗口等待循环的取消检查点。
- `backend/app/services/scientific_compute_coordinator.py`：有界 interactive/batch 计算门及可观测指标。
- `backend/app/main.py`：应用关闭时关闭共享协调器。
- `openspec/changes/add-background-full-analysis-orchestration/tasks.md`：仅勾选已由代码和自动化测试证明的有界协调点；其余任务保持未完成。

