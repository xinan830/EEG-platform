## Why

Review 已经拥有录音会话、播放游标和统一时间协调器，但算法面板目前只接入 PSD。独立 RBP 已完成统一算法执行通道整改，接入 Review 可以验证“Review 管理上下文、算法通道负责计算、面板负责展示”的完整边界。

## What Changes

- 从 Review 中删除旧的 PSD 算法面板入口和专属绑定代码。
- 在 Review 中增加显式的 RBP 算法面板入口。
- 让 RBP 复用 Review 的录音身份、导联快照和播放游标，通过现有按需窗口执行通道获取结果。
- 增加 Review 专用 RBP adapter 和结果面板；不复用算法工作区的独立录音、游标或调度状态。
- 保留 RBP 当前科学版本、窗口策略、单位、逐通道质量语义和后端 provenance。
- 在关闭面板、切换录音、切换导联和快速移动游标时清理或拒绝过期结果。

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `desktop/review-algorithm-link`: Review 可注册并展示通过验收的 RBP 算法面板，同时继续由 Review 独占播放游标和时间协调。

## Impact

- WPF Review 的算法 adapter、面板视图、Review ViewModel 和相关契约测试。
- 复用现有 `IAlgorithmClient`、RBP 动态窗口 API 和统一算法运行时；不新增后端科学计算路径。
- 不修改原始录音、波形显示通道、实时采集、后端 PSD 能力或 RBP 公式。
- 不增加旧结果格式、退役执行器或隐藏兼容回退；Review 不可用时显示结构化状态和原因。
- 这是新的用户可见面板行为，必须完成 WPF 构建、契约测试和人工回放验收后才能归档。
