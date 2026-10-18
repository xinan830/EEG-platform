# 长记录回溯性能 Phase 0 证据记录

日期：2026-10-18  
OpenSpec：`improve-long-recording-review-performance`  
当前提交：`2469a13`（性能诊断入口与基础阶段采样）

## 当前已证实

- WPF 桌面测试通过：725/725。
- `git diff --check` 通过。
- 回溯会话现在以 opt-in 方式记录导航目标、原始读取、滤波源、帧构建和
  SciChart 绑定阶段。
- 诊断样本包含 `recording_id`、`review_session_id`、revision、绝对时间范围、
  来源、通道数、点数和耗时；不包含原始 EEG 数值、完整响应或凭据。
- 现有回溯缓存仍有明确上限：原始窗口 2 个、帧 3 个、每条显示轨迹最多
  2000 个点。该事实来自代码检查，不等于已经证明真实 GPU 渲染没有瓶颈。

## 用户日志提供的事实

用户在长记录中观察到同一个 review 会话附近反复出现：

```text
POST /api/live-filters/sessions/{review-session}/warmup/binary?sample_count=2500
GET  /api/live-filters/sessions/{review-session}/checkpoint
```

这证明交互过程中发生了重复的滤波 warmup/checkpoint 请求，但仅凭 HTTP
日志还不能判断每次请求是否真正重新计算了相同窗口，也不能判断请求来自
哪个客户端回溯会话。新增的 `review_session_id` 和阶段来源字段用于完成这
个关联，不能把接口名称直接当作“重新计算”的证据。

## 尚未证实

- 10 秒、10 分钟、1 小时、24 小时和多日记录的实际性能矩阵。
- SciChart GPU 渲染、Dispatcher 耗时和 GC/内存曲线。
- 算法 `window-preview` 的完整结果查找/计算来源，以及全量结果是否被重复
  计算。
- 真实设备或用户机器上的多日回溯体验；自动化测试不能替代该验收。

## 根因决策

当前不能仅凭“`window-preview` 请求很多”或“warmup 请求很多”就决定实施
多分辨率索引。Phase 0 尚未完成根因判定。下一步必须在启用
`BRAIN_REVIEW_PERF_DIAGNOSTICS=1` 的桌面进程中，使用相同记录执行冷跳转、
反向/正向拖拽、连续播放和缓存命中操作，并按 `review_session_id` 汇总各
阶段耗时与重复次数。

在根因判定前，保留现有有界回溯路径，不修改滤波相位/边界语义，不引入
“拖慢游标”或未经证据支持的多分辨率存储层。

## 下一步

1. 补齐算法结果读取/计算来源的诊断字段。
2. 采集上述五档时长矩阵和至少一个多日 fixture 的自动化读盘证据。
3. 分别记录 SciChart 绑定耗时、点数峰值、挂起请求数量和进程工作集。
4. 只有在 Phase 0 报告给出根因后，才开始 Phase 1 的调度或 Phase 2 的
   多分辨率显示实现。

本记录不是临床或医学有效性结论；它只描述工程性能证据和当前未知项。
