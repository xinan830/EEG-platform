# 按需动态分析阶段 1：标量 WPF 验证

日期：2026-10-13

范围：Band Ratio、Peak Frequency、IAPF、Theta/Beta 的窗口投影与详情页状态绑定。
生产开关：保持默认关闭。报告中的自动化证据不包含代理实际操作 GUI；用户于
2026-10-13 回复“继续验收完毕”，已记录阶段 1 用户验收确认。

## 结果

- 后端标量能力与真实格式 API 对照：30 passed。
- 标量 WPF 专项：25 passed。
- RBP 按需回归专项：5 passed；与标量一起筛选运行 30 passed。
- WPF 全量测试：641 passed。
- WPF Release build：0 errors，6 个既有 NU1701 兼容性警告。
- `openspec validate on-demand-dynamic-analysis --strict`：通过。
- 文件规模检查：通过。
- `git diff --check`：通过（仅报告工作树已有的换行转换提示，无 whitespace error）。

## 覆盖边界

专项测试验证了窗口实际范围、单值轴/单位/shape、模块 evidence、Partial 有效值、
质量拒绝的 null 与定量失败信息、session/通道切换隔离、关闭重开、旧完整 Run 回退、
窗口替换以及 256 条有界历史。图表对 Partial 的有效值保留显示，不把拒绝或不可用结果
转换成零值。

## 未完成

阶段 1 用户验收已确认，进入阶段 2 证据文档整理。用户未提供逐操作截图或验收日志，
不据此补勾 PSD Review 的 GUI 专项；FAA、Brainbeat、STFT 尚未迁移。
